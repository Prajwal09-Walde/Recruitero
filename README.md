# Recruitero

An intelligent AI-powered recruitment platform designed to streamline the hiring process. Recruitero combines modern web technologies with advanced backend services to help organizations find and manage top talent efficiently.

## 🎯 Overview

Recruitero is a full-stack recruitment management system that leverages AI to enhance the recruitment workflow. Whether you're a recruiter, HR manager, or job seeker, Recruitero provides tools to make hiring smarter and faster.

**Live Demo**: [https://recruitai-frontend.vercel.app](https://recruitai-frontend.vercel.app)

## 🏗️ Architecture

Recruitero is built as a monorepo with the following key components:

### Backend (C# - 53.4%)
- Located in `/recruitai-backend`
- RESTful API service built with .NET
- Handles recruitment logic, data management, and AI integrations
- Database operations and business logic implementation

### Frontend (TypeScript - 41.7%)
- Located in `/recruitai-frontend`
- Modern web application deployed on Vercel
- Interactive user interface for recruiters and job seekers
- Real-time updates and responsive design

### Infrastructure (HCL/Terraform - 3.2%)
- Located in `/terraform`
- Infrastructure as Code for cloud deployment
- Automated provisioning and scaling configuration

## 🚀 Features

- **AI-Powered Matching**: Intelligent candidate-to-job matching
- **Recruitment Workflow**: Streamlined hiring process management
- **Candidate Management**: Track and organize candidates throughout the pipeline
- **Job Posting**: Easy job creation and distribution
- **Analytics**: Recruitment metrics and insights
- **Responsive Design**: Works seamlessly on desktop and mobile devices

## 🛠️ Tech Stack

| Component | Technologies |
|-----------|---|
| **Backend** | C#, .NET Framework |
| **Frontend** | TypeScript, React/Next.js, Modern Web Standards |
| **Infrastructure** | Terraform, Cloud Deployment |
| **Deployment** | Vercel (Frontend), Cloud Services (Backend) |

## 📋 Project Structure

```
Recruitero/
├── recruitai-backend/          # C# backend service
│   └── API endpoints and business logic
├── recruitai-frontend/         # TypeScript frontend application
│   └── Web UI and user interface
├── terraform/                  # Infrastructure configuration
│   └── Cloud resource definitions
├── scratch_inspect/            # Development and testing utilities
└── README.md                   # This file
```

## 🚀 Getting Started

### Prerequisites

- **.NET SDK** (for backend development)
- **Node.js** (v16 or higher, for frontend development)
- **Terraform** (for infrastructure management)

### Backend Setup

```bash
cd recruitai-backend
# Follow the backend-specific setup instructions
```

### Frontend Setup

```bash
cd recruitai-frontend
npm install
npm run dev
```

### Infrastructure Deployment

```bash
cd terraform
terraform init
terraform plan
terraform apply
```

## 🔧 Configuration

Environment variables are managed through:
- `.env` files for local development
- Platform-specific configurations (e.g., Vercel environment variables)
- See `vercel-env-vars.txt` for Vercel deployment setup

## 📚 Documentation

For detailed documentation, guides, and API specifications, please refer to the individual component directories:
- Backend documentation in `recruitai-backend/`
- Frontend documentation in `recruitai-frontend/`
- Infrastructure documentation in `terraform/`

## 🤝 Contributing

Contributions are welcome! Please feel free to:
1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📝 License

This project is open source. Please see the repository for license details.

## 🔗 Links

- **Live Application**: [https://recruitai-frontend.vercel.app](https://recruitai-frontend.vercel.app)
- **Repository**: [https://github.com/Prajwal09-Walde/Recruitero](https://github.com/Prajwal09-Walde/Recruitero)

## 👤 Author

**Prajwal09-Walde**
- GitHub: [@Prajwal09-Walde](https://github.com/Prajwal09-Walde)

## 📧 Support

For issues, questions, or suggestions, please open an issue on GitHub or contact the project maintainer.

---

**Made with ❤️ for smarter recruitment**
