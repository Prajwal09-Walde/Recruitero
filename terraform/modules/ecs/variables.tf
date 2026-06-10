variable "environment" {
  type        = string
  description = "Application deployment environment"
}

variable "vpc_id" {
  type        = string
  description = "VPC ID"
}

variable "public_subnet_ids" {
  type        = list(string)
  description = "List of public subnet IDs for ALB placement"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "List of private subnet IDs for ECS task placement"
}





variable "openai_api_key" {
  type        = string
  sensitive   = true
  description = "OpenAI API Key for task deployment"
}

variable "mongodb_uri" {
  type        = string
  sensitive   = true
  description = "MongoDB connection string URI"
}
