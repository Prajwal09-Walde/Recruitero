variable "aws_region" {
  type        = string
  default     = "us-east-1"
  description = "Target deployment region"
}

variable "environment" {
  type        = string
  default     = "production"
  description = "Environment name"
}

variable "vpc_cidr" {
  type        = string
  default     = "10.0.0.0/16"
  description = "CIDR block for VPC"
}

variable "openai_api_key" {
  type        = string
  sensitive   = true
  description = "OpenAI API Key for task execution"
}

variable "mongodb_uri" {
  type        = string
  sensitive   = true
  description = "MongoDB Atlas production connection string URI"
}
