import datetime
import os
import uuid
import logging
import zipfile
import re
import bcrypt
import jwt
import httpx
import time
from django.core.cache import cache
from rest_framework.decorators import api_view, authentication_classes, permission_classes
from rest_framework.response import Response
from rest_framework import status
from rest_framework.permissions import AllowAny, IsAuthenticated
from api.middleware import (
    JWTAuthentication, IsHRAdmin, IsTeamLead, IsViewer,
    IsHrAdminOrTeamLead, IsHrAdminOrTeamLeadOrViewer,
    UserAuthPayload, JWT_SECRET, JWT_ISSUER, JWT_AUDIENCE
)
from api.db import (
    users_col, jobs_col, job_postings_col, candidates_col,
    applications_col, interview_kits_col, webhook_configurations_col, webhook_deliveries_col
)
from api.services import (
    storage_service, enqueue_resume_pipeline, process_job_skill_extraction,
    notify_resume_uploaded, notify_processing_started
)

logger = logging.getLogger(__name__)

# ── AUTH CONTROLLER ───────────────────────────────────────────────────────────

def issue_tokens(user_doc: dict):
    # JWT Access Token
    expiry_minutes = int(os.getenv("Jwt__ExpiryMinutes") or 60)
    now_ts = int(time.time())
    exp_ts = now_ts + (expiry_minutes * 60)
    
    jti = str(uuid.uuid4())
    payload = {
        "sub": user_doc["email"],
        "email": user_doc["email"],
        "role": user_doc["role"],
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": user_doc["role"],
        "name": user_doc["fullName"],
        "jti": jti,
        "nbf": now_ts,
        "exp": exp_ts,
        "iss": JWT_ISSUER,
        "aud": JWT_AUDIENCE
    }
    
    token = jwt.encode(payload, JWT_SECRET, algorithm="HS256")
    
    # Refresh Token (Opaque base64 style random token)
    import base64
    raw_refresh = base64.b64encode(os.urandom(32)).decode('utf-8').replace('+', '-').replace('/', '_').replace('=', '')
    now = datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None)
    refresh_expiry = now + datetime.timedelta(days=7)
    
    users_col.update_one(
        {"_id": user_doc["_id"]},
        {"$set": {
            "refreshToken": raw_refresh,
            "refreshTokenExpiry": refresh_expiry.isoformat()
        }}
    )
    
    return {
        "token": token,
        "refreshToken": raw_refresh,
        "email": user_doc["email"],
        "role": user_doc["role"],
        "fullName": user_doc["fullName"]
    }

@api_view(["POST"])
@permission_classes([AllowAny])
def register_view(request):
    data = request.data
    email = data.get("email", "").strip().lower()
    password = data.get("password")
    full_name = data.get("fullName", "").strip()
    role = data.get("role")

    if not email or not password or not full_name or not role:
        return Response({"title": "Invalid request", "detail": "FullName, Email, Password, and Role are required.", "status": 400}, status=status.HTTP_400_BAD_REQUEST)

    if role not in ["HRAdmin", "TeamLead", "Viewer"]:
        return Response({"title": "Invalid request", "detail": "Role must be one of: HRAdmin, TeamLead, Viewer.", "status": 400}, status=status.HTTP_400_BAD_REQUEST)

    if len(password) < 6:
        return Response({"title": "Invalid request", "detail": "Password must be at least 6 characters.", "status": 400}, status=status.HTTP_400_BAD_REQUEST)

    existing = users_col.find_one({"email": email})
    if existing:
        return Response({
            "status": 409,
            "title": "Email already registered",
            "detail": "An account with that email address already exists. Please sign in instead."
        }, status=status.HTTP_409_CONFLICT)

    password_hash = bcrypt.hashpw(password.encode('utf-8'), bcrypt.gensalt()).decode('utf-8')
    
    user_id = str(uuid.uuid4())
    user_doc = {
        "_id": user_id,
        "fullName": full_name,
        "email": email,
        "passwordHash": password_hash,
        "role": role,
        "createdAt": datetime.datetime.utcnow().isoformat()
    }
    users_col.insert_one(user_doc)
    
    return Response(issue_tokens(user_doc))

@api_view(["POST"])
@permission_classes([AllowAny])
def login_view(request):
    data = request.data
    email = data.get("email", "").strip().lower()
    password = data.get("password")

    if not email or not password:
        return Response({"title": "Invalid request", "detail": "Email and Password are required.", "status": 400}, status=status.HTTP_400_BAD_REQUEST)

    user = users_col.find_one({"email": email})
    if not user or not bcrypt.checkpw(password.encode('utf-8'), user["passwordHash"].encode('utf-8')):
        return Response({
            "status": 401,
            "title": "Invalid credentials",
            "detail": "The email or password you entered is incorrect."
        }, status=status.HTTP_401_UNAUTHORIZED)

    users_col.update_one({"_id": user["_id"]}, {"$set": {"lastLoginAt": datetime.datetime.utcnow().isoformat()}})
    return Response(issue_tokens(user))

@api_view(["POST"])
@permission_classes([AllowAny])
def refresh_view(request):
    data = request.data
    email = data.get("email", "").strip().lower()
    refresh_token = data.get("refreshToken")

    if not email or not refresh_token:
        return Response({"title": "Invalid request", "detail": "Email and RefreshToken are required.", "status": 400}, status=status.HTTP_400_BAD_REQUEST)

    user = users_col.find_one({"email": email, "refreshToken": refresh_token})
    if not user:
        return Response({
            "status": 401,
            "title": "Invalid or expired refresh token",
            "detail": "Your session has expired. Please sign in again."
        }, status=status.HTTP_401_UNAUTHORIZED)

    # Check expiry
    expiry_str = user.get("refreshTokenExpiry")
    if expiry_str and datetime.datetime.fromisoformat(expiry_str) < datetime.datetime.utcnow():
        return Response({
            "status": 401,
            "title": "Invalid or expired refresh token",
            "detail": "Your session has expired. Please sign in again."
        }, status=status.HTTP_401_UNAUTHORIZED)

    return Response(issue_tokens(user))

@api_view(["POST"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsAuthenticated])
def logout_view(request):
    email = request.user.email
    users_col.update_one({"email": email}, {"$unset": {"refreshToken": "", "refreshTokenExpiry": ""}})
    return Response(status=status.HTTP_204_NO_CONTENT)

@api_view(["GET"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsAuthenticated])
def me_view(request):
    return Response({
        "email": request.user.email,
        "role": request.user.role,
        "fullName": request.user.full_name
    })

@api_view(["POST"])
@permission_classes([AllowAny])
def forgot_password_view(request):
    email = request.data.get("email", "").strip().lower()
    if not email:
        return Response({"title": "Invalid request", "detail": "Email is required.", "status": 400}, status=status.HTTP_400_BAD_REQUEST)

    user = users_col.find_one({"email": email})
    if user:
        reset_token = str(uuid.uuid4())
        expiry = datetime.datetime.utcnow() + datetime.timedelta(hours=1)
        users_col.update_one(
            {"_id": user["_id"]},
            {"$set": {
                "passwordResetToken": reset_token,
                "passwordResetExpiry": expiry.isoformat()
            }}
        )
        # Fallback to Console log email sending mimicking .NET host
        reset_link = f"http://localhost:3000/reset-password?email={email}&token={reset_token}"
        print(f"[Email Notification] Reset password link for {email}:\n{reset_link}")

    return Response({"message": "If your email is registered in our system, a password reset link has been sent to it."})

@api_view(["POST"])
@permission_classes([AllowAny])
def reset_password_view(request):
    data = request.data
    email = data.get("email", "").strip().lower()
    token = data.get("token")
    new_password = data.get("newPassword")

    if not email or not token or not new_password:
        return Response({"title": "Invalid request", "detail": "Email, Token, and NewPassword are required.", "status": 400}, status=status.HTTP_400_BAD_REQUEST)

    if len(new_password) < 6:
        return Response({"title": "Invalid request", "detail": "Password must be at least 6 characters.", "status": 400}, status=status.HTTP_400_BAD_REQUEST)

    user = users_col.find_one({"email": email, "passwordResetToken": token})
    if not user:
        return Response({
            "status": 400,
            "title": "Invalid or expired token",
            "detail": "The password reset token is invalid or has expired. Please request a new password reset."
        }, status=status.HTTP_400_BAD_REQUEST)

    expiry_str = user.get("passwordResetExpiry")
    if expiry_str and datetime.datetime.fromisoformat(expiry_str) < datetime.datetime.utcnow():
        return Response({
            "status": 400,
            "title": "Invalid or expired token",
            "detail": "The password reset token is invalid or has expired. Please request a new password reset."
        }, status=status.HTTP_400_BAD_REQUEST)

    new_hash = bcrypt.hashpw(new_password.encode('utf-8'), bcrypt.gensalt()).decode('utf-8')
    users_col.update_one(
        {"_id": user["_id"]},
        {
            "$set": {"passwordHash": new_hash},
            "$unset": {"passwordResetToken": "", "passwordResetExpiry": ""}
        }
    )
    return Response({"message": "Password has been reset successfully."})

# ── JOBS CONTROLLER ───────────────────────────────────────────────────────────

@api_view(["GET", "POST"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsAuthenticated])
def jobs_list_create_view(request):
    if request.method == "GET":
        # Any authenticated user can list active jobs
        jobs = list(jobs_col.find({"isActive": True}))
        return Response([{
            "id": j["_id"],
            "title": j["title"],
            "description": j["description"],
            "department": j.get("department", "Engineering"),
            "isActive": j.get("isActive", True),
            "createdAt": j.get("createdAt")
        } for j in jobs])

    # POST - Requires HRAdmin
    if request.user.role != "HRAdmin":
        return Response(status=status.HTTP_403_FORBIDDEN)

    data = request.data
    title = data.get("title", "").strip()
    description = data.get("description", "").strip()
    department = data.get("department", "Engineering").strip()

    if not title or not description:
        return Response("Title and Description are required.", status=status.HTTP_400_BAD_REQUEST)

    job_id = str(uuid.uuid4())
    now_iso = datetime.datetime.utcnow().isoformat()
    
    job_doc = {
        "_id": job_id,
        "title": title,
        "description": description,
        "department": department,
        "isActive": True,
        "createdAt": now_iso
    }
    jobs_col.insert_one(job_doc)

    # Save to JobPosting
    job_posting_doc = {
        "_id": job_id,
        "title": title,
        "description": description,
        "department": department,
        "isActive": True,
        "createdAt": now_iso,
        "skillGraph": None,
        "embeddingPointId": None
    }
    job_postings_col.insert_one(job_posting_doc)

    # Trigger async skill extraction
    import threading
    threading.Thread(target=process_job_skill_extraction, args=(job_id, title, description)).start()

    return Response({
        "id": job_id,
        "title": title,
        "description": description,
        "department": department,
        "isActive": True,
        "createdAt": now_iso
    }, status=status.HTTP_201_CREATED)

@api_view(["GET"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsAuthenticated])
def job_detail_view(request, job_id):
    job = jobs_col.find_one({"_id": job_id})
    if not job:
        return Response(status=status.HTTP_404_NOT_FOUND)

    posting = job_postings_col.find_one({"_id": job_id})
    
    return Response({
        "id": job["_id"],
        "title": job["title"],
        "description": job["description"],
        "department": job.get("department", "Engineering"),
        "isActive": job.get("isActive", True),
        "createdAt": job.get("createdAt"),
        "skillGraph": posting.get("skillGraph") if posting else None
    })

@api_view(["POST"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHRAdmin])
def import_dummy_jobs_view(request):
    fetched_jobs = []
    try:
        # Remotive API fetch
        headers = {"User-Agent": "RecruitAI-App/1.0"}
        res = httpx.get("https://remotive.com/api/remote-jobs?limit=5", headers=headers, timeout=10.0)
        if res.status_code == 200:
            data = res.json()
            if data.get("jobs"):
                fetched_jobs = data["jobs"]
    except Exception as e:
        logger.warning(f"Failed to fetch remote jobs: {e}")

    # Fallbacks if Remotive API fails
    if not fetched_jobs:
        fetched_jobs = [
            {
                "title": "QA Automation Engineer (Selenium & Cypress)",
                "description": "We are seeking a QA Automation Engineer to design, build, and maintain our automated testing frameworks...",
                "category": "qa"
            },
            {
                "title": "Data Scientist (AI & Machine Learning)",
                "description": "We are looking for a Data Scientist to join our analytics and intelligence team...",
                "category": "data"
            },
            {
                "title": "Senior UX/UI Product Designer",
                "description": "We are looking for a Senior UX/UI Product Designer to craft elegant, user-centric experiences...",
                "category": "design"
            },
            {
                "title": "DevOps & Cloud Infrastructure Engineer",
                "description": "We are looking for a DevOps Engineer to manage and scale our AWS cloud infrastructure...",
                "category": "engineering"
            },
            {
                "title": "Technical Product Manager (AI Platform)",
                "description": "We are looking for a Technical Product Manager to lead the roadmap for our AI-powered resume matching...",
                "category": "product"
            }
        ]

    imported = []
    for r_job in fetched_jobs:
        job_id = str(uuid.uuid4())
        title = r_job.get("title", "")
        desc = r_job.get("description", "")
        # Clean tags
        clean_desc = re.sub(r"<.*?>", " ", desc).strip()
        clean_desc = re.sub(r"\s+", " ", clean_desc)
        if len(clean_desc) < 100:
            clean_desc = clean_desc.ljust(100, '.')

        category = r_job.get("category", "engineering").lower()
        if any(x in category for x in ["software", "developer", "engineer", "engineering", "dev"]):
            dept = "Engineering"
        elif any(x in category for x in ["design", "ux", "ui"]):
            dept = "Design"
        elif "product" in category:
            dept = "Product"
        elif any(x in category for x in ["data", "science", "analytics"]):
            dept = "Data"
        elif any(x in category for x in ["qa", "test", "quality", "automation"]):
            dept = "QA"
        else:
            dept = "Management"

        now_iso = datetime.datetime.utcnow().isoformat()
        job_doc = {
            "_id": job_id,
            "title": title,
            "description": clean_desc,
            "department": dept,
            "isActive": True,
            "createdAt": now_iso
        }
        jobs_col.insert_one(job_doc)

        posting_doc = {
            "_id": job_id,
            "title": title,
            "description": clean_desc,
            "department": dept,
            "isActive": True,
            "createdAt": now_iso,
            "skillGraph": None,
            "embeddingPointId": None
        }
        job_postings_col.insert_one(posting_doc)

        # Trigger skill extraction thread
        import threading
        threading.Thread(target=process_job_skill_extraction, args=(job_id, title, clean_desc)).start()

        imported.append({
            "id": job_id,
            "title": title,
            "description": clean_desc,
            "department": dept,
            "isActive": True,
            "createdAt": now_iso
        })

    return Response(imported)

@api_view(["GET"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHrAdminOrTeamLead])
def analytics_view(request):
    jobs = list(jobs_col.find({"isActive": True}))
    apps = list(applications_col.find({}))

    total_jobs = len(jobs)
    total_apps = len(apps)

    funnel = {
        "Queued": 0, "Processing": 0, "Scored": 0,
        "SentToRecruiter": 0, "Shortlisted": 0, "Rejected": 0, "Failed": 0
    }
    for a in apps:
        status_val = a.get("status")
        if status_val in funnel:
            funnel[status_val] += 1

    # Department breakdown
    dep_jobs = {}
    for j in jobs:
        d = j.get("department", "Other")
        dep_jobs[d] = dep_jobs.get(d, 0) + 1

    job_to_dept = {j["_id"]: j.get("department", "Other") for j in jobs}
    dep_apps = {}
    for a in apps:
        d = job_to_dept.get(a.get("jobId"), "Other")
        dep_apps[d] = dep_apps.get(d, 0) + 1

    all_depts = set(dep_jobs.keys()).union(dep_apps.keys())
    departments = [{
        "department": d,
        "jobsCount": dep_jobs.get(d, 0),
        "applicationsCount": dep_apps.get(d, 0)
    } for d in all_depts]

    # Jobs breakdown
    jobs_breakdown = []
    for j in jobs:
        job_apps = [a for a in apps if a.get("jobId") == j["_id"]]
        scored_apps = [a for a in job_apps if a.get("fitScore") is not None]
        avg_score = sum(float(a["fitScore"]) for a in scored_apps) / len(scored_apps) if scored_apps else 0.0
        
        status_counts = {}
        for a in job_apps:
            st = a.get("status")
            status_counts[st] = status_counts.get(st, 0) + 1

        jobs_breakdown.append({
            "jobId": j["_id"],
            "title": j["title"],
            "department": j.get("department"),
            "applicationCount": len(job_apps),
            "averageScore": round(avg_score, 2),
            "statusCounts": status_counts
        })

    return Response({
        "totalJobs": total_jobs,
        "totalApplications": total_apps,
        "funnel": funnel,
        "departments": departments,
        "jobsBreakdown": jobs_breakdown
    })

@api_view(["GET"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHrAdminOrTeamLeadOrViewer])
def leaderboard_view(request, job_id):
    # Caching leaderboard for 30s
    page = int(request.query_string_params.get("page", 1) if hasattr(request, "query_string_params") else request.query_params.get("page", 1))
    page_size = int(request.query_string_params.get("pageSize", 20) if hasattr(request, "query_string_params") else request.query_params.get("pageSize", 20))
    status_filter = request.query_string_params.get("status", "All") if hasattr(request, "query_string_params") else request.query_params.get("status", "All")

    cache_key = f"leaderboard:{job_id}:{status_filter}:{page}:{page_size}:{request.user.role}:{request.user.email}"
    cached = cache.get(cache_key)
    if cached:
        return Response(cached)

    job = jobs_col.find_one({"_id": job_id})
    if not job:
        return Response(status=status.HTTP_404_NOT_FOUND)

    role = request.user.role
    email = request.user.email

    if role == "Viewer":
        candidate = candidates_col.find_one({"email": email})
        if not candidate:
            result = {"jobId": job_id, "jobTitle": job["title"], "totalApplicants": 0, "processedCount": 0, "candidates": []}
        else:
            app = applications_col.find_one({"jobId": job_id, "candidateId": candidate["_id"]})
            if not app:
                result = {"jobId": job_id, "jobTitle": job["title"], "totalApplicants": 0, "processedCount": 0, "candidates": []}
            else:
                result = {
                    "jobId": job_id,
                    "jobTitle": job["title"],
                    "totalApplicants": 1,
                    "processedCount": 1 if app.get("status") == "Scored" else 0,
                    "candidates": [{
                        "rank": app.get("rank") or 1,
                        "candidateId": candidate["_id"],
                        "name": candidate["fullName"],
                        "fitScore": float(app.get("fitScore") or 0.0),
                        "topSkillMatches": [],
                        "status": app.get("status"),
                        "applicationId": app["_id"]
                    }]
                }
    elif role == "TeamLead":
        # TeamLead can only see SentToRecruiter, Shortlisted, and Rejected
        valid_statuses = ["SentToRecruiter", "Shortlisted", "Rejected"]
        if status_filter != "All" and status_filter not in valid_statuses:
            result = {"jobId": job_id, "jobTitle": job["title"], "totalApplicants": 0, "processedCount": 0, "candidates": []}
        else:
            flt = {"jobId": job_id}
            if status_filter == "All":
                flt["status"] = {"$in": valid_statuses}
            else:
                flt["status"] = status_filter

            total_recruiter = applications_col.count_documents({"jobId": job_id, "status": {"$in": valid_statuses}})
            processed_recruiter = applications_col.count_documents({"jobId": job_id, "status": {"$in": ["Shortlisted", "Rejected"]}})

            apps = list(applications_col.find(flt).sort([("fitScore", -1), ("createdAt", 1)]).skip((page - 1) * page_size).limit(page_size))
            
            cands_list = []
            for idx, a in enumerate(apps):
                c = candidates_col.find_one({"_id": a["candidateId"]})
                cands_list.append({
                    "rank": a.get("rank") or (idx + 1),
                    "candidateId": a["candidateId"],
                    "name": c["fullName"] if c else "Unknown",
                    "fitScore": float(a.get("fitScore") or 0.0),
                    "topSkillMatches": [],
                    "status": a["status"],
                    "applicationId": a["_id"]
                })
            result = {
                "jobId": job_id,
                "jobTitle": job["title"],
                "totalApplicants": total_recruiter,
                "processedCount": processed_recruiter,
                "candidates": cands_list
            }
    else:
        # HRAdmin
        flt = {"jobId": job_id}
        if status_filter != "All":
            flt["status"] = status_filter

        total_applicants = applications_col.count_documents({"jobId": job_id})
        processed_count = applications_col.count_documents({"jobId": job_id, "status": "Scored"})

        apps = list(applications_col.find(flt).sort([("fitScore", -1), ("createdAt", 1)]).skip((page - 1) * page_size).limit(page_size))
        
        cands_list = []
        for idx, a in enumerate(apps):
            c = candidates_col.find_one({"_id": a["candidateId"]})
            cands_list.append({
                "rank": a.get("rank") or (idx + 1),
                "candidateId": a["candidateId"],
                "name": c["fullName"] if c else "Unknown",
                "fitScore": float(a.get("fitScore") or 0.0),
                "topSkillMatches": [],
                "status": a["status"],
                "applicationId": a["_id"]
            })
        result = {
            "jobId": job_id,
            "jobTitle": job["title"],
            "totalApplicants": total_applicants,
            "processedCount": processed_count,
            "candidates": cands_list
        }

    cache.set(cache_key, result, 30) # Cache for 30s
    return Response(result)

@api_view(["GET"])
@permission_classes([AllowAny])
def preview_skills_view(request):
    text = request.query_params.get("text", "").strip()
    if not text:
        return Response("Text query parameter is required.", status=status.HTTP_400_BAD_REQUEST)

    # Replicate skill extractor preview using GPT-4o
    try:
        result_json = extract_structured_data(
            system_prompt=JOB_SKILL_EXTRACTOR_SYSTEM,
            user_prompt=f"Job Title: Draft Job\n\nJob Description:\n{text}",
            schema=JOB_SKILL_SCHEMA,
            function_name="extract_skill_graph"
        )
        
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
        return Response(skill_graph)
    except Exception as e:
        logger.error(f"Error previewing skills: {e}")
        return Response({"error": str(e)}, status=status.HTTP_500_INTERNAL_SERVER_ERROR)

@api_view(["POST"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHrAdminOrTeamLeadOrViewer])
def bulk_upload_resumes_view(request, job_id):
    job_exists = jobs_col.find_one({"_id": job_id, "isActive": True})
    if not job_exists:
        return Response(status=status.HTTP_404_NOT_FOUND)

    files = request.FILES.getlist("files")
    if not files:
        return Response("No files uploaded", status=status.HTTP_400_BAD_REQUEST)

    candidate_email = None
    if request.user.role == "Viewer":
        candidate_email = request.user.email

    application_ids = []
    files_to_process = []

    # Unpack Zip
    for f in files:
        if f.name.lower().endswith(".zip"):
            try:
                with zipfile.ZipFile(f) as z:
                    for filename in z.namelist():
                        if filename.lower().endswith((".pdf", ".docx", ".txt")):
                            with z.open(filename) as zf:
                                content = zf.read()
                                # Wrap content in django-like file structure
                                class InMemoryFile:
                                    def __init__(self, name, data):
                                        self.name = name
                                        self.read = lambda: data
                                files_to_process.append(InMemoryFile(filename, content))
            except Exception as ex:
                logger.error(f"Failed to extract ZIP: {ex}")
        else:
            files_to_process.append(f)

    for f in files_to_process:
        try:
            file_bytes = f.read()
            # Upload to storage
            filename_clean = re.sub(r"[^\w\.-]", "_", f.name)
            s3_key = f"resumes/{job_id}/{uuid.uuid4()}_{filename_clean}"
            storage_service.upload(file_bytes, s3_key)

            # Derive Candidate Name
            cand_name = os.path.splitext(f.name)[0].replace("_", " ").replace("-", " ")
            cand_email = candidate_email or f"{uuid.uuid4()}@unknown.recruitai.io"

            # Upsert Candidate
            cand = candidates_col.find_one({"email": cand_email})
            if not cand:
                candidate_id = str(uuid.uuid4())
                candidates_col.insert_one({
                    "_id": candidate_id,
                    "fullName": cand_name,
                    "email": cand_email,
                    "createdAt": datetime.datetime.utcnow().isoformat()
                })
            else:
                candidate_id = cand["_id"]

            # Create Application
            app_id = str(uuid.uuid4())
            app_doc = {
                "_id": app_id,
                "jobId": job_id,
                "candidateId": candidate_id,
                "resumeS3Key": s3_key,
                "status": "Queued",
                "fitScore": None,
                "rank": None,
                "errorMessage": None,
                "retryCount": 0,
                "createdAt": datetime.datetime.utcnow().isoformat()
            }
            applications_col.insert_one(app_doc)
            application_ids.append(app_id)

            # Enqueue task
            enqueue_resume_pipeline(app_id)

            # Broadcast via websockets
            notify_resume_uploaded(job_id, app_id, cand_name)
        except Exception as ex:
            logger.error(f"Error processing file {f.name}: {ex}")

    estimated_minutes = len(application_ids) * 2
    return Response({
        "jobId": job_id,
        "applicationIds": application_ids,
        "processingTime": f"~{estimated_minutes} minutes"
    }, status=status.HTTP_202_ACCEPTED)

# ── APPLICATIONS CONTROLLER ───────────────────────────────────────────────────

@api_view(["GET"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHrAdminOrTeamLead])
def interview_kit_view(request, application_id):
    app = applications_col.find_one({"_id": application_id})
    kit = interview_kits_col.find_one({"applicationId": application_id}) if app else None

    if not app or not kit or not kit.get("questions"):
        # Not generated yet - return 404 with Retry-After header
        response_data = {
            "status": 404,
            "title": "Interview Kit Not Ready",
            "detail": f"The interview kit for application {application_id} has not been generated yet. Please retry after the processing completes.",
            "instance": request.path
        }
        res = Response(response_data, status=status.HTTP_404_NOT_FOUND)
        res["Retry-After"] = "120"
        return res

    cand = candidates_col.find_one({"_id": app.get("candidateId")})
    job = jobs_col.find_one({"_id": app.get("jobId")})

    # Format result matching .NET InterviewKitResult payload
    questions = [{
        "Category": q.get("category"),
        "Question": q.get("question"),
        "Difficulty": q.get("difficulty"),
        "Rationale": q.get("rationale")
    } for q in kit.get("questions", [])]

    return Response({
        "CandidateName": cand.get("fullName") if cand else "Unknown",
        "JobTitle": job.get("title") if job else "Unknown",
        "FitScore": float(app.get("fitScore") or 0.0),
        "Questions": questions
    })

@api_view(["POST"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHrAdminOrTeamLead])
def regenerate_interview_kit_view(request, application_id):
    app = applications_col.find_one({"_id": application_id})
    if not app:
        return Response(status=status.HTTP_404_NOT_FOUND)

    # Re-trigger processing asynchronously
    enqueue_resume_pipeline(application_id)

    return Response({
        "message": "Interview kit regeneration has been queued.",
        "applicationId": application_id
    }, status=status.HTTP_202_ACCEPTED)

@api_view(["PATCH"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsAuthenticated])
def update_application_status_view(request, application_id):
    new_status = request.data.get("status")
    if not new_status:
        return Response("Status is required.", status=status.HTTP_400_BAD_REQUEST)

    role = request.user.role
    if role == "Viewer":
        return Response(status=status.HTTP_403_FORBIDDEN)

    app = applications_col.find_one({"_id": application_id})
    if not app:
        return Response(status=status.HTTP_404_NOT_FOUND)

    if role == "TeamLead":
        # TL can only shortlist or reject, and current status must be SentToRecruiter, Shortlisted, or Rejected
        if new_status not in ["Shortlisted", "Rejected"]:
            return Response("Team Lead can only shortlist or reject candidates.", status=status.HTTP_400_BAD_REQUEST)

        curr_status = app.get("status")
        if curr_status not in ["SentToRecruiter", "Shortlisted", "Rejected"]:
            return Response("Candidate has not been sent to the Team Lead yet.", status=status.HTTP_400_BAD_REQUEST)

    applications_col.update_one({"_id": application_id}, {"$set": {"status": new_status, "updatedAt": datetime.datetime.utcnow().isoformat()}})
    
    # Recalculate leaderboard positions on status change
    recalculate_leaderboard_ranks(app.get("jobId"))

    return Response({"id": application_id, "status": new_status})

# ── WEBHOOKS CONTROLLER ───────────────────────────────────────────────────────

@api_view(["GET", "POST"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHRAdmin])
def webhooks_list_create_view(request):
    if request.method == "GET":
        tenant_id = request.query_params.get("tenantId")
        if not tenant_id:
            return Response("tenantId parameter is required", status=status.HTTP_400_BAD_REQUEST)
        configs = list(webhook_configurations_col.find({"tenantId": tenant_id, "isActive": True}))
        return Response([{
            "id": c["_id"],
            "tenantId": c["tenantId"],
            "targetUrl": c["targetUrl"],
            "atsType": c.get("atsType", "Custom"),
            "isActive": c.get("isActive", True),
            "events": c.get("events", []),
            "createdAt": c.get("createdAt")
        } for c in configs])

    # POST
    data = request.data
    tenant_id = data.get("tenantId")
    target_url = data.get("targetUrl")
    secret_key = data.get("secretKey")
    ats_type = data.get("atsType", "Custom")
    events = data.get("events") or ["candidate.scored"]

    if not tenant_id or not target_url or not secret_key:
        return Response("tenantId, targetUrl, and secretKey are required", status=status.HTTP_400_BAD_REQUEST)

    config_id = str(uuid.uuid4())
    now_iso = datetime.datetime.utcnow().isoformat()
    doc = {
        "_id": config_id,
        "tenantId": tenant_id,
        "targetUrl": target_url,
        "secretKey": secret_key,
        "atsType": ats_type,
        "isActive": True,
        "events": events,
        "createdAt": now_iso
    }
    webhook_configurations_col.insert_one(doc)

    return Response({
        "id": config_id,
        "tenantId": tenant_id,
        "targetUrl": target_url,
        "atsType": ats_type,
        "isActive": True,
        "events": events,
        "createdAt": now_iso
    }, status=status.HTTP_201_CREATED)

@api_view(["GET", "DELETE"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHRAdmin])
def webhook_detail_view(request, webhook_id):
    config = webhook_configurations_col.find_one({"_id": webhook_id})
    if not config:
        return Response(status=status.HTTP_404_NOT_FOUND)

    if request.method == "GET":
        return Response({
            "id": config["_id"],
            "tenantId": config["tenantId"],
            "targetUrl": config["targetUrl"],
            "atsType": config.get("atsType", "Custom"),
            "isActive": config.get("isActive", True),
            "events": config.get("events", []),
            "createdAt": config.get("createdAt")
        })

    # DELETE (Soft delete)
    webhook_configurations_col.update_one({"_id": webhook_id}, {"$set": {"isActive": False}})
    return Response(status=status.HTTP_204_NO_CONTENT)

@api_view(["GET"])
@authentication_classes([JWTAuthentication])
@permission_classes([IsHRAdmin])
def webhook_deliveries_view(request, webhook_id):
    # Paginated delivery logs
    page = int(request.query_params.get("page", 1))
    page_size = int(request.query_params.get("pageSize", 20))

    deliveries = list(webhook_deliveries_col.find({"configId": webhook_id}).sort("createdAt", -1).skip((page - 1) * page_size).limit(page_size))
    
    return Response([{
        "id": d["_id"],
        "configId": d["configId"],
        "eventType": d.get("eventType"),
        "responseCode": d.get("responseCode"),
        "attemptCount": d.get("attemptCount", 0),
        "deliveredSuccessfully": d.get("deliveredSuccessfully", False),
        "deliveredAt": d.get("deliveredAt"),
        "errorMessage": d.get("errorMessage"),
        "createdAt": d.get("createdAt")
    } for d in deliveries])
