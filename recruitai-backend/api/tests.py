import datetime
import time
import uuid
import jwt
from django.test import TestCase
from rest_framework.test import APIClient
from rest_framework import status
import api.db as db_module
from api.middleware import JWT_SECRET, JWT_ISSUER, JWT_AUDIENCE

class RecruitAiIntegrationTests(TestCase):
    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        # Override the database with integration test database
        cls.orig_db = db_module.db
        cls.test_db = db_module.client["recruitai_integration_tests"]
        
        # Point collections to the test db
        import api.views as views_module
        import api.middleware as middleware_module
        import api.services as services_module
        
        for module in [db_module, views_module, middleware_module, services_module]:
            if hasattr(module, "users_col"): module.users_col = cls.test_db["users"]
            if hasattr(module, "jobs_col"): module.jobs_col = cls.test_db["jobs"]
            if hasattr(module, "candidates_col"): module.candidates_col = cls.test_db["candidates"]
            if hasattr(module, "applications_col"): module.applications_col = cls.test_db["applications"]
            if hasattr(module, "interview_kits_col"): module.interview_kits_col = cls.test_db["interview_kits"]
            if hasattr(module, "job_postings_col"): module.job_postings_col = cls.test_db["job_postings"]
            if hasattr(module, "webhook_configurations_col"): module.webhook_configurations_col = cls.test_db["webhook_configurations"]
            if hasattr(module, "webhook_deliveries_col"): module.webhook_deliveries_col = cls.test_db["webhook_deliveries"]

        cls.test_job_id = "11111111-1111-1111-1111-111111111111"
        cls.test_app_id = "22222222-2222-2222-2222-222222222222"

    @classmethod
    def tearDownClass(cls):
        # Restore original collections
        import api.views as views_module
        import api.middleware as middleware_module
        import api.services as services_module

        for module in [db_module, views_module, middleware_module, services_module]:
            if hasattr(module, "users_col"): module.users_col = cls.orig_db["users"]
            if hasattr(module, "jobs_col"): module.jobs_col = cls.orig_db["jobs"]
            if hasattr(module, "candidates_col"): module.candidates_col = cls.orig_db["candidates"]
            if hasattr(module, "applications_col"): module.applications_col = cls.orig_db["applications"]
            if hasattr(module, "interview_kits_col"): module.interview_kits_col = cls.orig_db["interview_kits"]
            if hasattr(module, "job_postings_col"): module.job_postings_col = cls.orig_db["job_postings"]
            if hasattr(module, "webhook_configurations_col"): module.webhook_configurations_col = cls.orig_db["webhook_configurations"]
            if hasattr(module, "webhook_deliveries_col"): module.webhook_deliveries_col = cls.orig_db["webhook_deliveries"]
        super().tearDownClass()

    def setUp(self):
        self.client = APIClient()
        # Drop test database to ensure isolation
        db_module.client.drop_database("recruitai_integration_tests")
        
        # Seed basic test data mirroring C# RecruitAIWebAppFactory.cs
        # 1. Job
        db_module.jobs_col.insert_one({
            "_id": self.test_job_id,
            "title": "Senior .NET Engineer",
            "description": "Build AI products",
            "department": "Engineering",
            "isActive": True,
            "createdAt": datetime.datetime.utcnow().isoformat()
        })
        
        # 2. Candidate
        self.candidate_id = str(uuid.uuid4())
        db_module.candidates_col.insert_one({
            "_id": self.candidate_id,
            "fullName": "Jane Doe",
            "email": "jane@example.com",
            "createdAt": datetime.datetime.utcnow().isoformat()
        })

        # 3. Application
        db_module.applications_col.insert_one({
            "_id": self.test_app_id,
            "jobId": self.test_job_id,
            "candidateId": self.candidate_id,
            "resumeS3Key": "resumes/test/jane.pdf",
            "status": "Scored",
            "fitScore": 92.5,
            "rank": 1,
            "createdAt": datetime.datetime.utcnow().isoformat()
        })

        # 4. InterviewKit
        db_module.interview_kits_col.insert_one({
            "applicationId": self.test_app_id,
            "questions": [
                {"category": "Technical", "question": "Explain the CQRS pattern.", "difficulty": "Medium", "rationale": "Tests architecture knowledge."},
                {"category": "Behavioral", "question": "Describe a time you improved CI/CD.", "difficulty": "Easy", "rationale": "Assesses DevOps mindset."}
            ],
            "createdAt": datetime.datetime.utcnow().isoformat(),
            "updatedAt": datetime.datetime.utcnow().isoformat()
        })

    def generate_token(self, role="HRAdmin", email="admin@example.com"):
        payload = {
            "sub": email,
            "email": email,
            "role": role,
            "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": role,
            "name": "Test User",
            "jti": str(uuid.uuid4()),
            "exp": int(time.time()) + 3600,
            "iss": JWT_ISSUER,
            "aud": JWT_AUDIENCE
        }
        return jwt.encode(payload, JWT_SECRET, algorithm="HS256")

    def set_auth_token(self, role="HRAdmin", email="admin@example.com"):
        token = self.generate_token(role, email)
        self.client.credentials(HTTP_AUTHORIZATION=f"Bearer {token}")

    # ── Test Interview Kit Endpoints ──────────────────────────────────────────

    def test_get_interview_kit_returns_200_with_questions(self):
        self.set_auth_token("HRAdmin")
        response = self.client.get(f"/api/applications/{self.test_app_id}/interview-kit")
        self.assertEqual(response.status_code, status.HTTP_200_OK)
        
        data = response.json()
        self.assertEqual(data["CandidateName"], "Jane Doe")
        self.assertEqual(data["FitScore"], 92.5)
        self.assertTrue(len(data["Questions"]) >= 1)
        self.assertEqual(data["Questions"][0]["Question"], "Explain the CQRS pattern.")

    def test_get_interview_kit_not_found_returns_404_with_retry_after(self):
        self.set_auth_token("HRAdmin")
        random_app_id = str(uuid.uuid4())
        response = self.client.get(f"/api/applications/{random_app_id}/interview-kit")
        
        self.assertEqual(response.status_code, status.HTTP_404_NOT_FOUND)
        self.assertTrue("Retry-After" in response)
        self.assertEqual(response["Retry-After"], "120")

    def test_regenerate_interview_kit_hr_admin_returns_202(self):
        self.set_auth_token("HRAdmin")
        response = self.client.post(f"/api/applications/{self.test_app_id}/interview-kit/regenerate")
        self.assertEqual(response.status_code, status.HTTP_202_ACCEPTED)

    def test_regenerate_interview_kit_team_lead_returns_202(self):
        self.set_auth_token("TeamLead")
        response = self.client.post(f"/api/applications/{self.test_app_id}/interview-kit/regenerate")
        self.assertEqual(response.status_code, status.HTTP_202_ACCEPTED)

    def test_get_interview_kit_viewer_returns_403(self):
        self.set_auth_token("Viewer")
        response = self.client.get(f"/api/applications/{self.test_app_id}/interview-kit")
        # Viewers are not allowed in ApplicationsController [Authorize(Roles = HrAdmin, TeamLead)]
        self.assertEqual(response.status_code, status.HTTP_403_FORBIDDEN)

    # ── Test Leaderboard Endpoints ─────────────────────────────────────────────

    def test_get_leaderboard_returns_200_with_candidates(self):
        self.set_auth_token("HRAdmin")
        response = self.client.get(f"/api/jobs/{self.test_job_id}/leaderboard")
        self.assertEqual(response.status_code, status.HTTP_200_OK)
        
        data = response.json()
        self.assertEqual(data["jobId"], self.test_job_id)
        self.assertTrue(len(data["candidates"]) >= 1)
        self.assertEqual(data["candidates"][0]["fitScore"], 92.5)

    def test_get_leaderboard_not_found_returns_404(self):
        self.set_auth_token("HRAdmin")
        random_job_id = str(uuid.uuid4())
        response = self.client.get(f"/api/jobs/{random_job_id}/leaderboard")
        self.assertEqual(response.status_code, status.HTTP_404_NOT_FOUND)

    def test_get_leaderboard_unauthorized_returns_401(self):
        response = self.client.get(f"/api/jobs/{self.test_job_id}/leaderboard")
        self.assertEqual(response.status_code, status.HTTP_401_UNAUTHORIZED)

    def test_get_leaderboard_viewer_returns_200(self):
        self.set_auth_token("Viewer")
        response = self.client.get(f"/api/jobs/{self.test_job_id}/leaderboard")
        self.assertEqual(response.status_code, status.HTTP_200_OK)
