import os
import re
import math
import uuid
import json
import logging
import datetime
import hmac
import hashlib
from concurrent.futures import ThreadPoolExecutor
from io import BytesIO
from docx import Document
from pypdf import PdfReader
from openai import OpenAI
from qdrant_client import QdrantClient
from qdrant_client.http import models as qmodels
import httpx
from api.db import (
    jobs_col, job_postings_col, candidates_col, applications_col,
    interview_kits_col, webhook_configurations_col, webhook_deliveries_col
)

logger = logging.getLogger(__name__)

# Initialize background task executor
executor = ThreadPoolExecutor(max_workers=4)

# Initialize OpenAI and Qdrant Clients
OPENAI_API_KEY = os.getenv("OPENAI_API_KEY") or "test-api-key-for-tests"
OPENAI_ENDPOINT = os.getenv("OpenAI__Endpoint") or "https://api.openai.com/v1"

openai_client = OpenAI(api_key=OPENAI_API_KEY, base_url=OPENAI_ENDPOINT)

QDRANT_URL = os.getenv("Qdrant__Url") or "http://localhost:6333"
QDRANT_API_KEY = os.getenv("Qdrant__ApiKey")

if QDRANT_URL.startswith("http"):
    qdrant_client = QdrantClient(url=QDRANT_URL, api_key=QDRANT_API_KEY)
else:
    qdrant_client = QdrantClient(host=QDRANT_URL, port=6334, api_key=QDRANT_API_KEY)

# ── Local File / S3 Storage Mock ───────────────────────────────────────────────
# The .NET code has local file fallback or S3. Since we want to preserve exact behavior,
# we will write a storage service that saves files locally (under Storage__LocalPath)
# or mocks S3 uploads to local files.
STORAGE_LOCAL_PATH = os.getenv("Storage__LocalPath") or "uploads"

class LocalStorageService:
    def __init__(self, local_path=STORAGE_LOCAL_PATH):
        self.local_path = local_path
        if not os.path.exists(local_path):
            os.makedirs(local_path)

    def upload(self, file_bytes: bytes, key: str) -> str:
        full_path = os.path.join(self.local_path, key.replace("/", "_"))
        dir_name = os.path.dirname(full_path)
        if dir_name and not os.path.exists(dir_name):
            os.makedirs(dir_name)
        with open(full_path, "wb") as f:
            f.write(file_bytes)
        return key

    def download(self, key: str) -> bytes:
        full_path = os.path.join(self.local_path, key.replace("/", "_"))
        if os.path.exists(full_path):
            with open(full_path, "rb") as f:
                return f.read()
        return b""

storage_service = LocalStorageService()

# ── Resume Chunker ─────────────────────────────────────────────────────────────
class ResumeChunk:
    def __init__(self, section: str, text: str, token_count: int, chunk_index: int):
        self.section = section
        self.text = text
        self.token_count = token_count
        self.chunk_index = chunk_index

class ResumeChunker:
    MAX_TOKENS = 512
    OVERLAP_TOKENS = 50
    CHARS_PER_TOKEN = 4
    MAX_CHARS = MAX_TOKENS * CHARS_PER_TOKEN
    OVERLAP_CHARS = OVERLAP_TOKENS * CHARS_PER_TOKEN

    SECTION_HEADER_REGEX = re.compile(
        r"^(EXPERIENCE|WORK EXPERIENCE|PROFESSIONAL EXPERIENCE|"
        r"EDUCATION|ACADEMIC BACKGROUND|"
        r"SKILLS|TECHNICAL SKILLS|CORE COMPETENCIES|"
        r"PROJECTS|PERSONAL PROJECTS|OPEN SOURCE|"
        r"CERTIFICATIONS|CERTIFICATES|LICENSES|"
        r"SUMMARY|PROFILE|OBJECTIVE|ABOUT|"
        r"PUBLICATIONS|AWARDS|HONORS|"
        r"VOLUNTEER|LEADERSHIP|ACTIVITIES)\s*:?$",
        re.MULTILINE | re.IGNORECASE
    )

    def chunk(self, text: str) -> list:
        if not text or not text.strip():
            return []

        lines = text.split("\n")
        sections = self._split_into_sections(lines)
        chunks = []
        chunk_index = 0

        for section_name, section_text in sections:
            sub_chunks = self._split_with_overlap(section_text, self.MAX_CHARS, self.OVERLAP_CHARS)
            for sub_txt in sub_chunks:
                trimmed = sub_txt.strip()
                if len(trimmed) < 20:
                    continue
                token_count = int(math.ceil(len(trimmed) / self.CHARS_PER_TOKEN))
                chunks.append(ResumeChunk(
                    section=self._normalize_section(section_name),
                    text=trimmed,
                    token_count=token_count,
                    chunk_index=chunk_index
                ))
                chunk_index += 1

        return chunks

    def _split_into_sections(self, lines: list) -> list:
        sections = []
        current_section = "Header"
        current_lines = []

        for line in lines:
            if self.SECTION_HEADER_REGEX.match(line.strip()):
                if current_lines:
                    sections.append((current_section, "\n".join(current_lines)))
                current_section = line.strip().rstrip(":")
                current_lines = []
            else:
                current_lines.append(line)

        if current_lines:
            sections.append((current_section, "\n".join(current_lines)))

        return sections

    def _split_with_overlap(self, text: str, max_chars: int, overlap_chars: int) -> list:
        if len(text) <= max_chars:
            return [text]

        chunks = []
        start = 0
        while start < len(text):
            end = min(start + max_chars, len(text))
            if end < len(text):
                last_period = text.rfind(".", start, end)
                if last_period > start:
                    end = last_period + 1
            chunks.append(text[start:end])
            start = max(start + 1, end - overlap_chars)
        return chunks

    def _normalize_section(self, raw: str) -> str:
        s = raw.upper()
        if "EXPERIENCE" in s:
            return "Experience"
        if "EDUCATION" in s:
            return "Education"
        if "SKILL" in s:
            return "Skills"
        if "PROJECT" in s:
            return "Projects"
        if "CERT" in s:
            return "Certifications"
        if "SUMMARY" in s or "PROFILE" in s or "ABOUT" in s:
            return "Summary"
        return raw

# Prompts and schemas matching AiPrompts.cs
RESUME_METADATA_SYSTEM = (
    "You are a resume parsing AI. Extract structured metadata from the resume text.\n"
    "Be precise — only extract what is explicitly stated. Return only valid JSON.\n"
    "For skills, extract only named technologies, tools, and frameworks."
)

RESUME_METADATA_SCHEMA = {
    "type": "object",
    "properties": {
        "name": {"type": "string"},
        "email": {"type": "string", "format": "email"},
        "phone": {"type": "string"},
        "total_experience_years": {"type": "number", "minimum": 0},
        "skills": {"type": "array", "items": {"type": "string"}},
        "education": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "degree": {"type": "string"},
                    "institution": {"type": "string"},
                    "year": {"type": "integer"}
                }
            }
        },
        "last_role": {"type": "string"},
        "last_company": {"type": "string"}
    },
    "required": ["name", "total_experience_years", "skills", "last_role", "last_company"]
}

JOB_SKILL_EXTRACTOR_SYSTEM = (
    "You are a technical recruiter AI. Extract a structured skill graph from the\n"
    "job description. Be precise about weights (0.0–1.0). Return only valid JSON\n"
    "matching the schema provided. Weight skills by how critical they are:\n"
    "must-have → 0.8–1.0, strongly preferred → 0.5–0.79, nice-to-have → <0.5.\n"
    "Seniority must be one of: junior, mid, senior, staff, principal.\n"
    "Category must be one of: frontend, backend, cloud, data, mobile, devops,\n"
    "security, domain, soft, testing, ai."
)

JOB_SKILL_SCHEMA = {
    "type": "object",
    "properties": {
        "required_skills": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "skill": {"type": "string"},
                    "weight": {"type": "number", "minimum": 0.0, "maximum": 1.0},
                    "category": {"type": "string"}
                },
                "required": ["skill", "weight", "category"]
            }
        },
        "nice_to_have_skills": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "skill": {"type": "string"},
                    "weight": {"type": "number", "minimum": 0.0, "maximum": 1.0},
                    "category": {"type": "string"}
                },
                "required": ["skill", "weight", "category"]
            }
        },
        "experience_years_min": {"type": "integer", "minimum": 0},
        "seniority": {"type": "string", "enum": ["junior", "mid", "senior", "staff", "principal"]},
        "domain_keywords": {"type": "array", "items": {"type": "string"}},
        "job_embedding_text": {"type": "string"}
    },
    "required": ["required_skills", "nice_to_have_skills", "experience_years_min", "seniority", "domain_keywords", "job_embedding_text"]
}

CANDIDATE_RANKING_SYSTEM = (
    "You are a senior technical recruiter. Given a job description, skill requirements,\n"
    "and a candidate's resume analysis, write a concise hiring assessment.\n"
    "Be direct, factual, and highlight skill gaps honestly.\n"
    "Return only valid JSON matching the provided schema."
)

CANDIDATE_RANKING_SCHEMA = {
    "type": "object",
    "properties": {
        "summary": {"type": "string"},
        "strengths": {"type": "array", "items": {"type": "string"}},
        "gaps": {"type": "array", "items": {"type": "string"}},
        "recommendation": {"type": "string", "enum": ["Strong Yes", "Yes", "Maybe", "No"]},
        "confidence": {"type": "number"}
    },
    "required": ["summary", "strengths", "gaps", "recommendation", "confidence"]
}

INTERVIEW_KIT_SYSTEM = (
    "You are a technical interview designer. Generate targeted interview questions\n"
    "based on the job requirements and candidate profile. Match question difficulty\n"
    "to seniority level. Return ONLY valid JSON matching the schema exactly."
)

INTERVIEW_KIT_SCHEMA = {
    "type": "object",
    "properties": {
        "questions": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "category": {"type": "string"},
                    "question": {"type": "string"},
                    "difficulty": {"type": "string"},
                    "what_to_listen_for": {"type": "string"},
                    "targeted_gap": {"type": "string"}
                },
                "required": ["category", "question", "difficulty", "what_to_listen_for"]
            }
        }
    },
    "required": ["questions"]
}

# ── OpenAI Integration Helpers ──────────────────────────────────────────────────
def ensure_collection_exists(collection_name: str, size=1536):
    try:
        collections = [c.name for c in qdrant_client.get_collections().collections]
        if collection_name not in collections:
            logger.info(f"Creating Qdrant collection: {collection_name}")
            qdrant_client.create_collection(
                collection_name=collection_name,
                vectors_config=qmodels.VectorParams(
                    size=size,
                    distance=qmodels.Distance.COSINE,
                    on_disk=True if collection_name == "resumes" else False
                )
            )
            # Create payload indices
            if collection_name == "resumes":
                qdrant_client.create_payload_index(collection_name, "candidateId", qmodels.PayloadSchemaType.KEYWORD)
                qdrant_client.create_payload_index(collection_name, "jobId", qmodels.PayloadSchemaType.KEYWORD)
                qdrant_client.create_payload_index(collection_name, "applicationId", qmodels.PayloadSchemaType.KEYWORD)
    except Exception as e:
        logger.warning(f"Qdrant collection setup failed: {e}")

def get_embedding(text: str) -> list:
    res = openai_client.embeddings.create(
        input=[text],
        model="text-embedding-ada-002"
    )
    return res.data[0].embedding

def get_embeddings_batch(texts: list) -> list:
    res = openai_client.embeddings.create(
        input=texts,
        model="text-embedding-ada-002"
    )
    return [item.embedding for item in sorted(res.data, key=lambda x: x.index)]

def extract_structured_data(system_prompt: str, user_prompt: str, schema: dict, function_name: str):
    res = openai_client.chat.completions.create(
        model="gpt-4o",
        temperature=0.0,
        messages=[
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_prompt}
        ],
        tools=[
            {
                "type": "function",
                "function": {
                    "name": function_name,
                    "parameters": schema
                }
            }
        ],
        tool_choice={"type": "function", "function": {"name": function_name}}
    )
    args_str = res.choices[0].message.tool_calls[0].function.arguments
    return json.loads(args_str)

# ── Text Extractors ─────────────────────────────────────────────────────────────
def extract_text_from_pdf(pdf_bytes: bytes) -> str:
    try:
        reader = PdfReader(BytesIO(pdf_bytes))
        text = ""
        for page in reader.pages:
            t = page.extract_text()
            if t:
                text += t + "\n"
        return text
    except Exception as e:
        logger.error(f"Error parsing PDF: {e}")
        return ""

def extract_text_from_docx(docx_bytes: bytes) -> str:
    try:
        doc = Document(BytesIO(docx_bytes))
        return "\n".join([p.text for p in doc.paragraphs])
    except Exception as e:
        logger.error(f"Error parsing DOCX: {e}")
        return ""

def extract_text_from_txt(txt_bytes: bytes) -> str:
    return txt_bytes.decode("utf-8", errors="ignore")

# ── Fit Scoring ─────────────────────────────────────────────────────────────────
def cosine_similarity(v1: list, v2: list) -> float:
    dot = sum(a*b for a,b in zip(v1, v2))
    norm_a = math.sqrt(sum(a*a for a in v1))
    norm_b = math.sqrt(sum(b*b for b in v2))
    if norm_a * norm_b < 1e-10:
        return 0.0
    return dot / (norm_a * norm_b)

def match_keyword(text: str, keyword: str) -> bool:
    if not text or not keyword:
        return False
    if any(not c.isalnum() for c in keyword):
        return keyword.lower() in text.lower()
    pattern = rf"\b{re.escape(keyword)}\b"
    try:
        return bool(re.search(pattern, text, re.IGNORECASE))
    except Exception:
        return keyword.lower() in text.lower()

# Section weights multiplier matching FitScoringService.cs
SECTION_WEIGHTS = {
    "Skills": 1.3,
    "Experience": 1.2,
    "Projects": 1.1,
    "Summary": 1.0,
    "Certifications": 1.0,
    "Education": 0.8,
    "Header": 0.7
}

def compute_fit_score(job_id_str: str, candidate_id_str: str, application_id_str: str, full_resume_text: str) -> dict:
    try:
        # Load job posting
        job_post = job_postings_col.find_one({"_id": job_id_str})
        if not job_post:
            return {"fitScore": 0.0, "ranking": {"skillMatches": [], "topChunks": []}}

        skill_graph = job_post.get("skillGraph")
        
        # 1. Fetch Job Embedding
        ensure_collection_exists("job_postings")
        job_points = qdrant_client.scroll(
            collection_name="job_postings",
            scroll_filter=qmodels.Filter(
                must=[
                    qmodels.FieldCondition(key="jobId", match=qmodels.MatchValue(value=job_id_str))
                ]
            ),
            limit=1,
            with_vectors=True
        )[0]
        
        job_vector = None
        if job_points:
            job_vector = job_points[0].vector

        # 2. Fetch Resume Chunk vectors from Qdrant
        ensure_collection_exists("resumes")
        resume_points = []
        offset = None
        while True:
            scroll_res = qdrant_client.scroll(
                collection_name="resumes",
                scroll_filter=qmodels.Filter(
                    must=[
                        qmodels.FieldCondition(key="candidateId", match=qmodels.MatchValue(value=candidate_id_str)),
                        qmodels.FieldCondition(key="jobId", match=qmodels.MatchValue(value=job_id_str))
                    ]
                ),
                limit=100,
                offset=offset,
                with_vectors=True
            )
            resume_points.extend(scroll_res[0])
            offset = scroll_res[1]
            if not offset:
                break

        qdrant_failed = (job_vector is None or len(resume_points) == 0)

        # 3. Calculate Cosine similarities
        raw_score = 0.0
        top_chunks = []
        scored_chunks = []

        if not qdrant_failed:
            for p in resume_points:
                sec = p.payload.get("section", "Unknown")
                txt = p.payload.get("text", "")
                weight = SECTION_WEIGHTS.get(sec, 1.0)
                sim = cosine_similarity(job_vector, p.vector)
                scored_chunks.append({
                    "section": sec,
                    "text": txt,
                    "similarity": sim,
                    "weight": weight,
                    "weighted_similarity": sim * weight
                })

            # Sort descending by weighted similarity
            scored_chunks.sort(key=lambda x: x["weighted_similarity"], reverse=True)
            top_chunks = scored_chunks[:5]

            # Weighted average
            total_weight = sum(c["weight"] for c in top_chunks)
            weighted_score = sum(c["weighted_similarity"] for c in top_chunks) / total_weight if total_weight > 0 else 0
            raw_score = weighted_score * 100.0

        # 4. Blend with Lexical matching
        boost = 0.0
        penalty = 0.0
        skill_matches = []
        blended_score = raw_score

        if skill_graph:
            req_skills = skill_graph.get("requiredSkills", [])
            nice_skills = skill_graph.get("niceToHaveSkills", [])
            dom_keywords = skill_graph.get("domainKeywords", [])

            # Check required
            total_req_w = 0.0
            matched_req_w = 0.0
            for s in req_skills:
                skill_name = s.get("skill")
                w = s.get("weight") or 1.0
                matched = match_keyword(full_resume_text, skill_name)
                total_req_w += w
                if matched:
                    matched_req_w += w
                
                # Derive match similarity or default to 0.5/1.0
                match_score = 0.0
                if matched:
                    # Look for max similarity in scored chunks that contain the skill
                    chunk_sims = [c["similarity"] for c in scored_chunks if skill_name.lower() in c["text"].lower()]
                    match_score = max(chunk_sims) if chunk_sims else 1.0

                skill_matches.append({
                    "skill": skill_name,
                    "matched": matched,
                    "matchScore": match_score
                })

            # Check nice-to-have
            total_nice_w = 0.0
            matched_nice_w = 0.0
            for s in nice_skills:
                skill_name = s.get("skill")
                w = s.get("weight") or 1.0
                matched = match_keyword(full_resume_text, skill_name)
                total_nice_w += w
                if matched:
                    matched_nice_w += w

            # Check domain keywords
            total_dom_w = 0.0
            matched_dom_w = 0.0
            for kw in dom_keywords:
                matched = match_keyword(full_resume_text, kw)
                w = 0.5
                total_dom_w += w
                if matched:
                    matched_dom_w += w

            req_score = (matched_req_w / total_req_w) * 100.0 if total_req_w > 0 else 100.0
            nice_score = (matched_nice_w / total_nice_w) * 100.0 if total_nice_w > 0 else 100.0
            dom_score = (matched_dom_w / total_dom_w) * 100.0 if total_dom_w > 0 else 100.0

            lexical_score = (req_score * 0.6) + (nice_score * 0.3) + (dom_score * 0.1)
            
            if qdrant_failed:
                blended_score = lexical_score
            else:
                blended_score = (raw_score * 0.7) + (lexical_score * 0.3)

            # Penalty if no required skills match
            any_required_matched = any(sm["matched"] for sm in skill_matches)
            if req_skills and not any_required_matched:
                penalty = 10.0

        final_score = max(0.0, min(100.0, blended_score + boost - penalty))
        final_score = round(final_score, 2)

        top_chunks_payload = [{
            "section": c["section"],
            "similarity": round(c["similarity"], 4),
            "textPreview": c["text"][:200]
        } for c in top_chunks]

        ranking = {
            "fitScore": final_score,
            "topChunks": top_chunks_payload,
            "skillMatches": skill_matches,
            "scoredAt": datetime.datetime.utcnow().isoformat()
        }

        return {
            "fitScore": final_score,
            "ranking": ranking
        }
    except Exception as e:
        logger.error(f"Error computing fit score: {e}")
        return {"fitScore": 0.0, "ranking": {"skillMatches": [], "topChunks": []}}

# ── Webhook Signer & Dispatcher ─────────────────────────────────────────────────
class WebhookDispatcher:
    @staticmethod
    def compute_signature(payload_json: str, secret_key: str) -> str:
        key_bytes = secret_key.encode('utf-8')
        payload_bytes = payload_json.encode('utf-8')
        sig = hmac.new(key_bytes, payload_bytes, hashlib.sha256).hexdigest()
        return "sha256=" + sig.lower()

    @staticmethod
    def dispatch(config_id_str: str, target_url: str, secret_key: str, payload: dict):
        payload_json = json.dumps(payload, separators=(',', ':'))
        signature = WebhookDispatcher.compute_signature(payload_json, secret_key)
        
        # Save WebhookDelivery log
        delivery_id = str(uuid.uuid4())
        delivery_doc = {
            "_id": delivery_id,
            "configId": config_id_str,
            "payload": payload_json,
            "eventType": payload.get("event"),
            "responseCode": None,
            "responseBody": None,
            "attemptCount": 0,
            "deliveredSuccessfully": False,
            "deliveredAt": None,
            "errorMessage": None,
            "createdAt": datetime.datetime.utcnow().isoformat()
        }
        webhook_deliveries_col.insert_one(delivery_doc)

        # Retry logic: 3 attempts with 5s, 30s, 120s backoff
        retry_delays = [5, 30, 120]
        
        def run_dispatch():
            attempt = 0
            headers = {
                "Content-Type": "application/json",
                "X-RecruitAI-Signature": signature,
                "X-RecruitAI-Event": payload.get("event"),
                "X-RecruitAI-Delivery-Id": delivery_id
            }

            while attempt < 3:
                attempt += 1
                try:
                    res = httpx.post(target_url, content=payload_json, headers=headers, timeout=10.0)
                    webhook_deliveries_col.update_one(
                        {"_id": delivery_id},
                        {
                            "$set": {
                                "responseCode": res.status_code,
                                "responseBody": res.text[:500],
                                "deliveredSuccessfully": 200 <= res.status_code < 300,
                                "deliveredAt": datetime.datetime.utcnow().isoformat() if 200 <= res.status_code < 300 else None
                            },
                            "$inc": {"attemptCount": 1}
                        }
                    )

                    if 200 <= res.status_code < 300:
                        logger.info(f"Webhook delivered: {target_url} ({res.status_code})")
                        return
                except Exception as ex:
                    webhook_deliveries_col.update_one(
                        {"_id": delivery_id},
                        {
                            "$set": {"errorMessage": str(ex)},
                            "$inc": {"attemptCount": 1}
                        }
                    )
                if attempt < 3:
                    import time
                    time.sleep(retry_delays[attempt - 1])

        # Run dispatch in thread executor
        executor.submit(run_dispatch)

# ── Real-time Hub Notifications Mock ───────────────────────────────────────────
# We will mock the hub context notifications. When these are called, they should
# broadcast WebSocket messages to the channels group if Django Channels is running.
def get_channel_layer():
    try:
        from channels.layers import get_channel_layer as gcl
        return gcl()
    except Exception:
        return None

def broadcast_hub_message(job_id_str: str, target: str, args: list):
    layer = get_channel_layer()
    if layer:
        from asgiref.sync import async_to_sync
        group_name = f"job_{job_id_str}"
        async_to_sync(layer.group_send)(
            group_name,
            {
                "type": "hub.message",
                "target": target,
                "arguments": args
            }
        )

def notify_resume_uploaded(job_id_str: str, app_id_str: str, candidate_name: str):
    broadcast_hub_message(
        job_id_str,
        "ResumeUploaded",
        [app_id_str, candidate_name, datetime.datetime.utcnow().isoformat()]
    )

def notify_processing_started(job_id_str: str, app_id_str: str, candidate_name: str):
    broadcast_hub_message(
        job_id_str,
        "ProcessingStarted",
        [app_id_str, candidate_name]
    )

def notify_fit_score_ready(job_id_str: str, app_id_str: str, candidate_name: str, fit_score: float, rank: int):
    broadcast_hub_message(
        job_id_str,
        "FitScoreReady",
        [app_id_str, candidate_name, float(fit_score), int(rank)]
    )

def notify_interview_kit_ready(job_id_str: str, app_id_str: str):
    broadcast_hub_message(
        job_id_str,
        "InterviewKitReady",
        [app_id_str]
    )

def notify_processing_failed(job_id_str: str, app_id_str: str, candidate_name: str, err: str):
    broadcast_hub_message(
        job_id_str,
        "ProcessingFailed",
        [app_id_str, candidate_name, err]
    )

# ── Leaderboard Rank Recalculation ──────────────────────────────────────────────
def recalculate_leaderboard_ranks(job_id_str: str):
    """Sort scored applications and update their ranks in MongoDB."""
    apps = list(applications_col.find({"jobId": job_id_str, "status": "Scored"}).sort("fitScore", -1))
    for idx, app in enumerate(apps):
        applications_col.update_one({"_id": app["_id"]}, {"$set": {"rank": idx + 1}})

# ── Job Skill Extraction Process ────────────────────────────────────────────────
def process_job_skill_extraction(job_id_str: str, title: str, description: str):
    try:
        # Extract skills via GPT-4o
        logger.info(f"Extracting skill graph for job {job_id_str}")
        user_prompt = f"Job Title: {title}\n\nJob Description:\n{description}"
        result_json = extract_structured_data(
            system_prompt=JOB_SKILL_EXTRACTOR_SYSTEM,
            user_prompt=user_prompt,
            schema=JOB_SKILL_SCHEMA,
            function_name="extract_skill_graph"
        )
        
        # Mapping properties camelCase for frontend
        required_skills = []
        for s in result_json.get("required_skills", []):
            required_skills.append({
                "skill": s.get("skill"),
                "weight": s.get("weight"),
                "category": s.get("category")
            })

        nice_to_have_skills = []
        for s in result_json.get("nice_to_have_skills", []):
            nice_to_have_skills.append({
                "skill": s.get("skill"),
                "weight": s.get("weight"),
                "category": s.get("category")
            })

        skill_graph = {
            "requiredSkills": required_skills,
            "niceToHaveSkills": nice_to_have_skills,
            "experienceYearsMin": result_json.get("experience_years_min", 0),
            "seniority": result_json.get("seniority", "mid"),
            "domainKeywords": result_json.get("domain_keywords", []),
            "jobEmbeddingText": result_json.get("job_embedding_text", ""),
            "extractedAt": datetime.datetime.utcnow().isoformat()
        }

        # Embed job posting representation
        logger.info(f"Generating job embedding for {job_id_str}")
        embedding_text = skill_graph.get("jobEmbeddingText") or f"{title} {description}"
        job_embedding = get_embedding(embedding_text)

        # Upsert Qdrant point
        ensure_collection_exists("job_postings")
        point_id = str(uuid.uuid4())
        qdrant_client.upsert(
            collection_name="job_postings",
            points=[
                qmodels.PointStruct(
                    id=point_id,
                    vector=job_embedding,
                    payload={
                        "jobId": job_id_str,
                        "type": "job_posting",
                        "indexedAt": datetime.datetime.utcnow().isoformat()
                    }
                )
            ]
        )

        # Save to database
        job_postings_col.update_one(
            {"_id": job_id_str},
            {"$set": {
                "skillGraph": skill_graph,
                "embeddingPointId": point_id
            }},
            upsert=True
        )
        logger.info(f"Skill graph applied to JobPosting {job_id_str}")
    except Exception as e:
        logger.error(f"Error processing job skill extraction: {e}")

# ── Resume AI pipeline Background Executor ──────────────────────────────────────
def process_resume_pipeline(application_id_str: str):
    logger.info(f"Starting pipeline for application {application_id_str}")
    app = applications_col.find_one({"_id": application_id_str})
    if not app:
        return

    job_id_str = app.get("jobId")
    candidate_id_str = app.get("candidateId")
    
    # Resolve Candidate Name
    cand = candidates_col.find_one({"_id": candidate_id_str})
    candidate_name = cand.get("fullName") if cand else "Unknown"

    try:
        # Mark as Processing
        applications_col.update_one({"_id": application_id_str}, {"$set": {"status": "Processing"}})
        notify_processing_started(job_id_str, application_id_str, candidate_name)

        # Download from Storage
        file_bytes = storage_service.download(app.get("resumeS3Key"))
        ext = os.path.splitext(app.get("resumeS3Key"))[1].lower()

        # Extract text
        if ext == ".docx":
            extracted_text = extract_text_from_docx(file_bytes)
        elif ext == ".txt":
            extracted_text = extract_text_from_txt(file_bytes)
        else:
            extracted_text = extract_text_from_pdf(file_bytes)

        # Update text in DB
        applications_col.update_one({"_id": application_id_str}, {"$set": {"extractedText": extracted_text}})

        # 1. Embed Resume Chunks
        chunker = ResumeChunker()
        chunks = chunker.chunk(extracted_text)
        if chunks:
            chunk_texts = [c.text for c in chunks]
            embeddings = get_embeddings_batch(chunk_texts)
            
            ensure_collection_exists("resumes")
            points = []
            for idx, c in enumerate(chunks):
                point_id = str(uuid.uuid4())
                points.append(
                    qmodels.PointStruct(
                        id=point_id,
                        vector=embeddings[idx],
                        payload={
                            "candidateId": candidate_id_str,
                            "applicationId": application_id_str,
                            "jobId": job_id_str,
                            "section": c.section,
                            "chunkIndex": c.chunk_index,
                            "tokenCount": c.token_count,
                            "text": c.text[:500]
                        }
                    )
                )
            
            # Batch upsert
            qdrant_client.upsert(collection_name="resumes", points=points)

        # 2. Extract metadata via GPT-4o
        truncated_text = extracted_text[:6000] if len(extracted_text) > 6000 else extracted_text
        metadata = extract_structured_data(
            system_prompt=RESUME_METADATA_SYSTEM,
            user_prompt=f"Resume:\n{truncated_text}",
            schema=RESUME_METADATA_SCHEMA,
            function_name="extract_resume_metadata"
        )
        
        extracted_name = metadata.get("name") or candidate_name
        extracted_email = metadata.get("email") or (cand.get("email") if cand else f"{uuid.uuid4()}@unknown.recruitai.io")
        
        # Update Candidate detail
        candidates_col.update_one(
            {"_id": candidate_id_str},
            {"$set": {
                "fullName": extracted_name,
                "email": extracted_email,
                "updatedAt": datetime.datetime.utcnow().isoformat()
            }}
        )

        # 3. Fit Scoring
        score_res = compute_fit_score(job_id_str, candidate_id_str, application_id_str, extracted_text)
        fit_score = score_res["fitScore"]
        
        # 4. Generate Ranking narrative (strengths/gaps)
        job_post = job_postings_col.find_one({"_id": job_id_str})
        req_skills_names = [s.get("skill") for s in job_post.get("skillGraph", {}).get("requiredSkills", [])] if job_post else []
        seniority = job_post.get("skillGraph", {}).get("seniority", "mid") if job_post else "mid"

        ranking_context_prompt = (
            f"Job: {job_post.get('title') if job_post else 'Job Posting'}\n"
            f"Seniority level: {seniority}\n"
            f"Required skills: {req_skills_names}\n\n"
            f"Candidate: {extracted_name}, {metadata.get('total_experience_years', 0):.1f} years experience\n"
            f"Fit score: {fit_score}/100\n"
            f"Skill match results: {score_res['ranking']['skillMatches']}\n"
        )
        
        ranking_json = extract_structured_data(
            system_prompt=CANDIDATE_RANKING_SYSTEM,
            user_prompt=ranking_context_prompt,
            schema=CANDIDATE_RANKING_SCHEMA,
            function_name="extract_candidate_evaluation"
        )

        # Update application status to Scored, and set fit score
        applications_col.update_one(
            {"_id": application_id_str},
            {"$set": {
                "status": "Scored",
                "fitScore": fit_score,
                "rankingNarrative": ranking_json
            }}
        )
        
        # Recalculate leaderboard ranks
        recalculate_leaderboard_ranks(job_id_str)
        
        # Fetch updated rank
        updated_app = applications_col.find_one({"_id": application_id_str})
        rank = updated_app.get("rank") or 1

        notify_fit_score_ready(job_id_str, application_id_str, extracted_name, fit_score, rank)

        # 5. Generate Interview Kit
        kit_prompt = (
            f"Job Title: {job_post.get('title') if job_post else 'Job Posting'} ({seniority} level)\n"
            f"Candidate: {extracted_name}, {metadata.get('total_experience_years', 0):.1f} years exp\n"
            f"Fit Score: {fit_score}/100\n"
            f"Required skills (probe these): {req_skills_names[:8]}\n"
            f"Identified skill gaps: {[sm['skill'] for sm in score_res['ranking']['skillMatches'] if not sm['matched']][:3]}\n"
        )

        kit_json = extract_structured_data(
            system_prompt=INTERVIEW_KIT_SYSTEM,
            user_prompt=kit_prompt,
            schema=INTERVIEW_KIT_SCHEMA,
            function_name="generate_interview_kit"
        )

        # Convert questions matching domain schema
        questions = []
        for q in kit_json.get("questions", []):
            questions.append({
                "category": q.get("category", "Technical"),
                "question": q.get("question"),
                "difficulty": q.get("difficulty", "Medium"),
                "rationale": q.get("what_to_listen_for") or ""
            })

        interview_kits_col.replace_one(
            {"applicationId": application_id_str},
            {
                "applicationId": application_id_str,
                "questions": questions,
                "createdAt": datetime.datetime.utcnow().isoformat(),
                "updatedAt": datetime.datetime.utcnow().isoformat()
            },
            upsert=True
        )

        notify_interview_kit_ready(job_id_str, application_id_str)

        # 6. Webhook dispatch
        tenant_id = str(uuid.UUID(int=0)) # Dummy tenant ID matching .NET code
        configs = list(webhook_configurations_col.find({"tenantId": tenant_id, "isActive": True}))
        
        if configs:
            webhook_payload = {
                "event": "candidate.scored",
                "timestamp": datetime.datetime.utcnow().isoformat(),
                "jobId": job_id_str,
                "externalJobId": configs[0].get("externalJobId"),
                "candidate": {
                    "name": extracted_name,
                    "email": extracted_email,
                    "fitScore": fit_score,
                    "recommendation": ranking_json.get("recommendation", "Maybe"),
                    "topStrengths": ranking_json.get("strengths", []),
                    "gaps": ranking_json.get("gaps", []),
                    "interviewKitUrl": f"https://app.recruitai.io/kits/{application_id_str}"
                }
            }
            for cfg in configs:
                if "candidate.scored" in cfg.get("events", []):
                    WebhookDispatcher.dispatch(cfg["_id"], cfg["targetUrl"], cfg["secretKey"], webhook_payload)

    except Exception as e:
        logger.exception(f"Error processing resume for application {application_id_str}: {e}")
        applications_col.update_one(
            {"_id": application_id_str},
            {
                "$set": {
                    "status": "Failed",
                    "errorMessage": str(e)
                },
                "$inc": {"retryCount": 1}
            }
        )
        notify_processing_failed(job_id_str, application_id_str, candidate_name, str(e))

def enqueue_resume_pipeline(application_id_str: str):
    executor.submit(process_resume_pipeline, application_id_str)
