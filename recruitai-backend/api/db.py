import os
import logging
from pymongo import MongoClient, ASCENDING, DESCENDING
from dotenv import load_dotenv

load_dotenv()

logger = logging.getLogger(__name__)

# Fetch MongoDB URI from environment variables (standardizing on MONGODB_URI first, then ASP.NET style ConnectionStrings__MongoDB)
MONGODB_URI = os.getenv("MONGODB_URI") or os.getenv("ConnectionStrings__MongoDB")

if not MONGODB_URI:
    # Fallback to local default if not set
    MONGODB_URI = "mongodb://localhost:27017/recruitai"

# Initialize Client
# Optimize latency & pool properties similar to .NET settings
client = MongoClient(
    MONGODB_URI,
    serverSelectionTimeoutMS=5000,
    connectTimeoutMS=5000,
    maxIdleTimeMS=25 * 60 * 1000,
    minPoolSize=5,
    maxPoolSize=100
)

# Extract Database Name (default to 'recruitai' if not specified in URI)
try:
    default_db = client.get_default_database()
    db_name = default_db.name if default_db else "recruitai"
except Exception:
    db_name = "recruitai"

if not db_name or db_name == "admin":
    db_name = "recruitai"

db = client[db_name]

# Collections mapping
users_col = db["users"]
jobs_col = db["jobs"]
candidates_col = db["candidates"]
applications_col = db["applications"]
interview_kits_col = db["interview_kits"]
job_postings_col = db["job_postings"]
webhook_configurations_col = db["webhook_configurations"]
webhook_deliveries_col = db["webhook_deliveries"]

def create_indexes():
    """Create MongoDB indexes asynchronously/fail-safe, matching .NET Program.cs logic."""
    try:
        # Unique sparse index on Candidate Email
        candidates_col.create_index([("email", ASCENDING)], unique=True, sparse=True)
        
        # Compound index on Application JobId + FitScore + CreatedAt
        applications_col.create_index([
            ("jobId", ASCENDING),
            ("fitScore", DESCENDING),
            ("createdAt", ASCENDING)
        ])
        
        # Compound index on Application JobId + Status + FitScore + CreatedAt
        applications_col.create_index([
            ("jobId", ASCENDING),
            ("status", ASCENDING),
            ("fitScore", DESCENDING),
            ("createdAt", ASCENDING)
        ])
        
        # Unique index on AppUser Email
        users_col.create_index([("email", ASCENDING)], unique=True)
        
        # Unique sparse index on InterviewKit ApplicationId
        interview_kits_col.create_index([("applicationId", ASCENDING)], unique=True, sparse=True)
        
        logger.info("MongoDB indexes created successfully.")
    except Exception as e:
        logger.error(f"Failed to create MongoDB indexes: {e}")

# Run index creation
create_indexes()
