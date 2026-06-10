variable "environment" {
  type        = string
  description = "Application deployment environment"
}

variable "vpc_id" {
  type        = string
  description = "VPC ID"
}

variable "private_subnet_ids" {
  type        = list(string)
  description = "List of private subnet IDs for Redis placement"
}

variable "ecs_security_group_id" {
  type        = string
  description = "Security group of ECS service allowing Redis access"
}
