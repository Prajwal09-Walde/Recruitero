resource "aws_elasticache_subnet_group" "main" {
  name        = "recruitai-redis-subnet-group-${var.environment}"
  subnet_ids  = var.private_subnet_ids
  description = "Subnet group for RecruitAI Redis cluster"
}

resource "aws_security_group" "redis" {
  name        = "recruitai-redis-sg-${var.environment}"
  description = "Security group for Redis cache cluster"
  vpc_id      = var.vpc_id

  ingress {
    description     = "Allow Redis access from ECS tasks only"
    from_port       = 6379
    to_port         = 6379
    protocol        = "tcp"
    security_groups = [var.ecs_security_group_id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name        = "recruitai-redis-sg-${var.environment}"
    Environment = var.environment
  }
}

resource "aws_elasticache_replication_group" "redis" {
  replication_group_id          = "recruitai-redis-${var.environment}"
  replication_group_description = "Redis replication group for SignalR and cache"
  node_type                     = "cache.t3.micro"
  port                          = 6379
  parameter_group_name          = "default.redis7"
  subnet_group_name             = aws_elasticache_subnet_group.main.name
  security_group_ids            = [aws_security_group.redis.id]
  automatic_failover_enabled    = true
  num_cache_clusters            = 2

  tags = {
    Environment = var.environment
  }
}
