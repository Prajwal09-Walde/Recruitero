namespace RecruitAI.Infrastructure.AI;

/// <summary>
/// All GPT-4o system prompts and function schemas as compile-time constants.
/// Centralizing prompts here makes them easy to version-control and test.
/// </summary>
public static class AiPrompts
{
    // ── Job Skill Extraction ─────────────────────────────────────────────────────

    public const string JobSkillExtractorSystem =
        """
        You are a technical recruiter AI. Extract a structured skill graph from the
        job description. Be precise about weights (0.0–1.0). Return only valid JSON
        matching the schema provided. Weight skills by how critical they are: 
        must-have → 0.8–1.0, strongly preferred → 0.5–0.79, nice-to-have → <0.5.
        Seniority must be one of: junior, mid, senior, staff, principal.
        Category must be one of: frontend, backend, cloud, data, mobile, devops, 
        security, domain, soft, testing, ai.
        """;

    /// <summary>
    /// GPT-4o function definition for skill graph extraction.
    /// Uses function calling for guaranteed structured output.
    /// </summary>
    public const string SkillGraphFunctionSchema = """
        {
          "name": "extract_skill_graph",
          "description": "Extract a structured skill graph from a job description",
          "parameters": {
            "type": "object",
            "properties": {
              "required_skills": {
                "type": "array",
                "description": "Skills that are mandatory for the role",
                "items": {
                  "type": "object",
                  "properties": {
                    "skill":    { "type": "string", "description": "Exact skill name, e.g. 'React.js'" },
                    "weight":   { "type": "number", "minimum": 0.0, "maximum": 1.0 },
                    "category": { "type": "string" }
                  },
                  "required": ["skill", "weight", "category"]
                }
              },
              "nice_to_have_skills": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "skill":    { "type": "string" },
                    "weight":   { "type": "number", "minimum": 0.0, "maximum": 1.0 },
                    "category": { "type": "string" }
                  },
                  "required": ["skill", "weight", "category"]
                }
              },
              "experience_years_min": {
                "type": "integer",
                "description": "Minimum years of professional experience required",
                "minimum": 0
              },
              "seniority": {
                "type": "string",
                "enum": ["junior", "mid", "senior", "staff", "principal"]
              },
              "domain_keywords": {
                "type": "array",
                "description": "Domain/industry terms, e.g. ['fintech', 'trading', 'real-time']",
                "items": { "type": "string" }
              },
              "job_embedding_text": {
                "type": "string",
                "description": "A synthesized 300-word representation of the role for semantic embedding. Include all key skills, responsibilities, domain, and seniority context."
              }
            },
            "required": ["required_skills", "nice_to_have_skills", "experience_years_min",
                         "seniority", "domain_keywords", "job_embedding_text"]
          }
        }
        """;

    // ── Candidate Ranking Narrative ───────────────────────────────────────────────

    public const string CandidateRankingSystem =
        """
        You are a senior technical recruiter. Given a job description, skill requirements,
        and a candidate's resume analysis, write a concise hiring assessment.
        Be direct, factual, and highlight skill gaps honestly.
        Return only valid JSON matching the provided schema.
        """;

    public const string CandidateRankingResponseSchema = """
        {
          "summary": "3-sentence hiring assessment",
          "strengths": ["strength 1", "strength 2", "strength 3"],
          "gaps": ["gap 1", "gap 2"],
          "recommendation": "Strong Yes | Yes | Maybe | No",
          "confidence": 0.85
        }
        """;

    // ── Interview Kit Generation ──────────────────────────────────────────────────

    public const string InterviewKitSystem =
        """
        You are a technical interview designer. Generate targeted interview questions
        based on the job requirements and candidate profile. Match question difficulty
        to seniority level. Return ONLY valid JSON matching the schema exactly.
        """;

    public const string InterviewKitResponseSchema = """
        {
          "questions": [
            {
              "category": "Technical | Behavioral | System Design | Domain",
              "question": "The interview question text",
              "difficulty": "Easy | Medium | Hard",
              "what_to_listen_for": "Key signals to evaluate in the answer",
              "targeted_gap": "Optional: skill or gap this question probes"
            }
          ]
        }
        """;

    // ── Resume Metadata Extraction ────────────────────────────────────────────────

    public const string ResumeMetadataSystem =
        """
        You are a resume parsing AI. Extract structured metadata from the resume text.
        Be precise — only extract what is explicitly stated. Return only valid JSON.
        For skills, extract only named technologies, tools, and frameworks.
        """;

    public const string ResumeMetadataFunctionSchema = """
        {
          "name": "extract_resume_metadata",
          "description": "Extract structured metadata from a resume",
          "parameters": {
            "type": "object",
            "properties": {
              "name":                    { "type": "string" },
              "email":                   { "type": "string", "format": "email" },
              "phone":                   { "type": "string" },
              "total_experience_years":  { "type": "number", "minimum": 0 },
              "skills": {
                "type": "array",
                "items": { "type": "string" }
              },
              "education": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "degree":      { "type": "string" },
                    "institution": { "type": "string" },
                    "year":        { "type": "integer" }
                  }
                }
              },
              "last_role":    { "type": "string" },
              "last_company": { "type": "string" }
            },
            "required": ["name", "total_experience_years", "skills", "last_role", "last_company"]
          }
        }
        """;
}
