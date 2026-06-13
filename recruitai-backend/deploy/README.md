# 🚀 Recruitero Backend Deployment Guide

This guide outlines how to deploy the **Recruitero** ASP.NET Core backend to a publicly accessible Linux VPS (Virtual Private Server) such as DigitalOcean, AWS EC2, or Linode, and connect it with your Vercel frontend.

---

## Prerequisites
* A Linux server (recommended: **Ubuntu 22.04 LTS**).
* A registered domain name (e.g., `yourdomain.com`) pointing to your server's public IP address.
* Connection credentials for MongoDB Atlas and OpenAI API.

---

## Step 1: Server Setup
SSH into your server and run the following commands to update packages and install essential dependencies:
```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y git curl rsync unzip
```

---

## Step 2: Install .NET 8 SDK & Runtime
Install the .NET SDK to enable compiling and running the ASP.NET Core application:
```bash
# Add Microsoft package signing key
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install .NET SDK
sudo apt update
sudo apt install -y dotnet-sdk-8.0
```
Verify the installation:
```bash
dotnet --version
```

---

## Step 3: Configure Directories & Environment
1. Create the deployment directory structure:
   ```bash
   sudo mkdir -p /var/www/recruitai/current
   sudo mkdir -p /var/www/recruitai/publish
   sudo mkdir -p /var/www/recruitai/backup
   sudo chown -R $USER:$USER /var/www/recruitai
   ```

2. Clone your repository into `/var/www/recruitai/repo`:
   ```bash
   git clone <YOUR_GIT_REPO_URL> /var/www/recruitai/repo
   ```

3. Create the environment configuration file:
   Copy `/var/www/recruitai/repo/recruitai-backend/deploy/.env.template` to `/var/www/recruitai/current/.env` and edit it to include your secrets:
   ```bash
   cp /var/www/recruitai/repo/recruitai-backend/deploy/.env.template /var/www/recruitai/current/.env
   nano /var/www/recruitai/current/.env
   ```
   Ensure you fill in:
   - `JWT_SECRET` (minimum 32-character long secret key)
   - `MONGODB_URI` (your MongoDB Atlas connection string)
   - `OPENAI_API_KEY` (your OpenAI API key)
   - `Smtp:Host`, `Smtp:Port`, `Smtp:Username`, `Smtp:Password` (for sending emails)

---

## Step 4: Setup Nginx Reverse Proxy & SSL (Certbot)
1. Install Nginx and Certbot:
   ```bash
   sudo apt install -y nginx certbot python3-certbot-nginx
   ```

2. Copy the Nginx configuration template from `/var/www/recruitai/repo/recruitai-backend/deploy/nginx.conf` to `/etc/nginx/sites-available/recruitai`:
   ```bash
   sudo cp /var/www/recruitai/repo/recruitai-backend/deploy/nginx.conf /etc/nginx/sites-available/recruitai
   ```

3. Edit the file to replace `recruitai.io` and `www.recruitai.io` with your custom domain name:
   ```bash
   sudo nano /etc/nginx/sites-available/recruitai
   ```

4. Enable the configuration and restart Nginx:
   ```bash
   sudo ln -s /etc/nginx/sites-available/recruitai /etc/nginx/sites-enabled/
   sudo rm /etc/nginx/sites-enabled/default || true
   sudo nginx -t
   sudo systemctl restart nginx
   ```

5. Request an SSL Certificate from Let's Encrypt:
   ```bash
   sudo certbot --nginx -d yourdomain.com -d www.yourdomain.com
   ```

---

## Step 5: Configure the Systemd Background Service
1. Copy the systemd service file:
   ```bash
   sudo cp /var/www/recruitai/repo/recruitai-backend/deploy/recruitai.service /etc/systemd/system/recruitai.service
   ```

2. Reload systemd and enable the service:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable recruitai
   ```

---

## Step 6: Deploy Using the Automation Script
Make `deploy.sh` executable and run it to compile and launch the backend:
```bash
chmod +x /var/www/recruitai/repo/recruitai-backend/deploy/deploy.sh
/var/www/recruitai/repo/recruitai-backend/deploy/deploy.sh
```
The script will publish the backend, stop the background service, deploy the new binaries, adjust permissions, restart the service, and verify the health check. If the health check fails, it automatically rolls back to the previous version.

---

## Step 7: Update Vercel Environment Variables
Once your public backend is up and running (e.g., at `https://yourdomain.com`), configure your Vercel project to connect to it:

1. Open your Vercel Dashboard, navigate to **Project Settings** -> **Environment Variables**.
2. Add/Edit the following variables:
   - **`NEXT_PUBLIC_API_URL`**: Set to `https://yourdomain.com`
   - **`NEXT_PUBLIC_HUB_URL`**: Set to `https://yourdomain.com/hubs/recruitment`
3. Trigger a redeployment of your frontend on Vercel to bake in the new URLs.
