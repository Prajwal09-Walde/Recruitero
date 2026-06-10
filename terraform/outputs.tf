output "alb_dns_name" {
  value       = module.ecs.alb_dns_name
  description = "Public Load Balancer DNS entry point"
}

output "ecr_repository_url" {
  value       = module.ecs.ecr_repository_url
  description = "ECR Repository URL for Docker API images"
}


