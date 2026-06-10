variable "environment" {
  type        = string
  description = "Application deployment environment (e.g. prod, staging)"
}

variable "vpc_cidr" {
  type        = string
  description = "VPC CIDR block"
  default     = "10.0.0.0/16"
}
