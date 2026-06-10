output "alb_dns_name" {
  value = aws_lb.alb.dns_name
}

output "ecr_repository_url" {
  value = aws_ecr_repository.api.repository_url
}

output "ecs_security_group_id" {
  value = aws_security_group.ecs_tasks.id
}
