#!/usr/bin/env bash

# Exit immediately if a command exits with a non-zero status
set -e

echo "===================================================="
echo "🚀 Starting RecruitAI Backend Deployment Automation"
echo "===================================================="

# Define paths
APP_DIR="/var/www/recruitai"
PUBLISH_DIR="/var/www/recruitai/publish"
BACKUP_DIR="/var/www/recruitai/backup"
SRC_DIR="/var/www/recruitai/repo" # Assuming the git repo is fetched here

# Step 1: Navigate to repository source and build
if [ -d "$SRC_DIR" ]; then
    echo "📂 Navigating to source repository..."
    cd "$SRC_DIR"
    echo "📥 Pulling latest codebase..."
    git pull
else
    echo "⚠️ Source repository path $SRC_DIR not found. Assuming local publish setup..."
fi

echo "🏗️ Building and publishing solution in Release mode..."
dotnet publish recruitai-backend/RecruitAI.sln -c Release -o "$PUBLISH_DIR"

# Step 2: Stop systemd service to prevent file locks
echo "🛑 Stopping recruitai systemd service..."
sudo systemctl stop recruitai || true

# Step 3: Backup current working deployment
if [ -d "$APP_DIR/current" ]; then
    echo "📦 Backing up current deployment to $BACKUP_DIR..."
    rm -rf "$BACKUP_DIR"
    mkdir -p "$BACKUP_DIR"
    cp -r "$APP_DIR/current/." "$BACKUP_DIR/"
fi

# Step 4: Deploy new binaries
echo "🚚 Deploying published binaries to $APP_DIR/current..."
mkdir -p "$APP_DIR/current"
# Copy published files, keeping existing .env configuration
rsync -av --exclude='.env' "$PUBLISH_DIR/" "$APP_DIR/current/"

# Step 5: Adjust permissions
echo "🔑 Setting directory permissions to www-data..."
sudo chown -R www-data:www-data "$APP_DIR"

# Step 6: Restart systemd daemon and service
echo "🔄 Reloading systemd configurations..."
sudo systemctl daemon-reload
echo "⚡ Starting recruitai systemd service..."
sudo systemctl start recruitai
sudo systemctl enable recruitai

# Step 7: Verification
echo "⏳ Waiting 3 seconds for Kestrel to initialize..."
sleep 3

echo "🔍 Running application health check..."
HEALTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health || echo "FAILED")

if [ "$HEALTH_STATUS" = "200" ]; then
    echo "✅ Success: Health check passed with HTTP 200!"
else
    echo "❌ Error: Health check failed with status: $HEALTH_STATUS. Rolling back..."
    # Rollback execution
    sudo systemctl stop recruitai
    cp -r "$BACKUP_DIR/." "$APP_DIR/current/"
    sudo chown -R www-data:www-data "$APP_DIR"
    sudo systemctl start recruitai
    echo "⚠️ Rollback to previous version completed."
    exit 1
fi

echo "===================================================="
echo "🎉 RecruitAI Backend Deployed Successfully!"
echo "===================================================="
