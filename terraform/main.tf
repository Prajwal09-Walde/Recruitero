terraform {
  required_version = ">= 1.7.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

# 1. VPC and Subnets Module
module "networking" {
  source      = "./modules/networking"
  environment = var.environment
  vpc_cidr    = var.vpc_cidr
}

# 2. ECS Cluster, ALB, Task and Service Module
module "ecs" {
  source             = "./modules/ecs"
  environment        = var.environment
  vpc_id             = module.networking.vpc_id
  public_subnet_ids  = module.networking.public_subnet_ids
  private_subnet_ids = module.networking.private_subnet_ids
  openai_api_key     = var.openai_api_key
  mongodb_uri        = var.mongodb_uri
}
