# ECR Repository
resource "aws_ecr_repository" "api" {
  name                 = "recruitai-api-${var.environment}"
  image_tag_mutability = "MUTABLE"
  image_scanning_configuration {
    scan_on_push = true
  }
}

# ECS Cluster
resource "aws_ecs_cluster" "main" {
  name = "recruitai-cluster-${var.environment}"
}

# CloudWatch Log Group for ECS logs
resource "aws_cloudwatch_log_group" "ecs" {
  name              = "/ecs/recruitai-api-${var.environment}"
  retention_in_days = 30
}

# Secrets Manager configuration
resource "aws_secretsmanager_secret" "api_secrets" {
  name        = "recruitai-api-secrets-${var.environment}"
  description = "Sensitive configurations for RecruitAI API"
}

resource "aws_secretsmanager_secret_version" "api_secrets_val" {
  secret_id = aws_secretsmanager_secret.api_secrets.id
  secret_string = jsonencode({
    OpenAiApiKey     = var.openai_api_key
    MongoDbUri       = var.mongodb_uri
  })
}

# IAM Roles
resource "aws_iam_role" "ecs_execution" {
  name = "recruitai-ecs-execution-role-${var.environment}"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "ecs_execution_policy" {
  role       = aws_iam_role.ecs_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# Give task execution role access to Secrets Manager
resource "aws_iam_policy" "ecs_execution_secrets" {
  name        = "recruitai-ecs-execution-secrets-${var.environment}"
  description = "Allows ECS execution role to retrieve secrets from Secrets Manager"

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "secretsmanager:GetSecretValue"
        ]
        Resource = [
          aws_secretsmanager_secret.api_secrets.arn
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "ecs_execution_secrets_attach" {
  role       = aws_iam_role.ecs_execution.name
  policy_arn = aws_iam_policy.ecs_execution_secrets.arn
}

resource "aws_iam_role" "ecs_task" {
  name = "recruitai-ecs-task-role-${var.environment}"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })
}

# Grant task role S3 and Secrets Manager permissions
resource "aws_iam_policy" "ecs_task_policy" {
  name        = "recruitai-ecs-task-policy-${var.environment}"
  description = "Grant task S3 bucket read/write and Secrets access"

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "s3:GetObject",
          "s3:PutObject",
          "s3:DeleteObject",
          "s3:ListBucket"
        ]
        Resource = [
          "arn:aws:s3:::recruitai-resumes",
          "arn:aws:s3:::recruitai-resumes/*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "secretsmanager:GetSecretValue"
        ]
        Resource = [
          aws_secretsmanager_secret.api_secrets.arn
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "ecs_task_policy_attach" {
  role       = aws_iam_role.ecs_task.name
  policy_arn = aws_iam_policy.ecs_task_policy.arn
}

# Security Groups
resource "aws_security_group" "alb" {
  name        = "recruitai-alb-sg-${var.environment}"
  description = "Security group for ALB allowing HTTP and HTTPS traffic"
  vpc_id      = var.vpc_id

  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name        = "recruitai-alb-sg-${var.environment}"
    Environment = var.environment
  }
}

resource "aws_security_group" "ecs_tasks" {
  name        = "recruitai-ecs-sg-${var.environment}"
  description = "Security group for ECS tasks allowing traffic from ALB"
  vpc_id      = var.vpc_id

  ingress {
    from_port       = 5000
    to_port         = 5000
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name        = "recruitai-ecs-tasks-sg-${var.environment}"
    Environment = var.environment
  }
}

# Load Balancer
resource "aws_lb" "alb" {
  name               = "recruitai-alb-${var.environment}"
  internal           = false
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb.id]
  subnets            = var.public_subnet_ids

  tags = {
    Environment = var.environment
  }
}

resource "aws_lb_target_group" "target_group" {
  name        = "recruitai-tg-${var.environment}"
  port        = 5000
  protocol    = "HTTP"
  vpc_id      = var.vpc_id
  target_type = "ip"

  health_check {
    path                = "/swagger/index.html"
    port                = "5000"
    protocol            = "HTTP"
    matcher             = "200"
    interval            = 30
    timeout             = 5
    healthy_threshold   = 3
    unhealthy_threshold = 3
  }

  tags = {
    Environment = var.environment
  }
}

# ACM Certificate fetch
data "aws_acm_certificate" "cert" {
  domain      = "recruitai.io"
  statuses    = ["ISSUED"]
  most_recent = true
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.alb.arn
  port              = "80"
  protocol          = "HTTP"

  default_action {
    type = "redirect"

    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.alb.arn
  port              = "443"
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-2016-08"
  certificate_arn   = data.aws_acm_certificate.cert.arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.target_group.arn
  }
}

# ECS Task Definition
resource "aws_ecs_task_definition" "api" {
  family                   = "recruitai-api-task-${var.environment}"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "512"
  memory                   = "1024"
  execution_role_arn       = aws_iam_role.ecs_execution.arn
  task_role_arn            = aws_iam_role.ecs_task.arn

  container_definitions = jsonencode([
    {
      name      = "recruitai-api"
      image     = "${aws_ecr_repository.api.repository_url}:latest"
      essential = true
      portMappings = [
        {
          containerPort = 5000
          hostPort      = 5000
        }
      ]
      environment = [
        {
          name  = "Qdrant__Host"
          value = "qdrant.recruitai.io" # Assume resolved or set
        },
        {
          name  = "Qdrant__Port"
          value = "6334"
        }
      ]
      secrets = [
        {
          name      = "OpenAI__ApiKey"
          valueFrom = "${aws_secretsmanager_secret.api_secrets.arn}:OpenAiApiKey::"
        },
        {
          name      = "ConnectionStrings__MongoDB"
          valueFrom = "${aws_secretsmanager_secret.api_secrets.arn}:MongoDbUri::"
        }
      ]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.ecs.name
          "awslogs-region"        = "us-east-1"
          "awslogs-stream-prefix" = "api"
        }
      }
    }
  ])
}

# ECS Service
resource "aws_ecs_service" "api" {
  name            = "recruitai-api-service-${var.environment}"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 2
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [aws_security_group.ecs_tasks.id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.target_group.arn
    container_name   = "recruitai-api"
    container_port   = 5000
  }

  depends_on = [aws_lb_listener.https]
}
