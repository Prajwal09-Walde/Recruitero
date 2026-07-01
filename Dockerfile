# Use official Python slim runtime as parent image
FROM python:3.12-slim

# Set environment variables
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

# Set work directory
WORKDIR /app

# Copy requirements from recruitai-backend
COPY recruitai-backend/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy the rest of backend files
COPY recruitai-backend/ .

# Expose port (Render sets $PORT dynamically)
EXPOSE 8080

# Run using Daphne ASGI server on port 8080 (or bind to $PORT)
CMD ["sh", "-c", "daphne -b 0.0.0.0 -p ${PORT:-8080} recruitai_backend.asgi:application"]
