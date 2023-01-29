variable "environment_suffix" {
  type        = string
  description = "The suffix to append to the environment name. For example \"dev\" or \"prod\""
}

terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "=3.39.1"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "=2.22.0"
    }
  }
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
}

module "web_app" {
  source = "./web_app"
  environment_suffix = var.environment_suffix
}

module "database" {
  source = "./database"
  environment_suffix = var.environment_suffix
}