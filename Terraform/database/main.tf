variable "environment_suffix" {}

data "azurerm_resource_group" "resource_group" {
  name = "webvite-rg-${var.environment_suffix}"
}

data "azurerm_key_vault" "key_vault" {
  name                = "webvite-kv-${var.environment_suffix}"
  resource_group_name = "webvite-rg-${var.environment_suffix}"
}

data "azurerm_key_vault_secret" "sql_server_password" {
  name         = "sql-server-password"
  key_vault_id = data.azurerm_key_vault.key_vault.id 
}

resource "azurerm_mssql_server" "sql_server" {
  name                         = "webvite-sqls-${var.environment_suffix}"
  resource_group_name          = data.azurerm_resource_group.resource_group.name
  location                     = data.azurerm_resource_group.resource_group.location
  version                      = "12.0"
  administrator_login          = "4dm1n157r470r"
  administrator_login_password = data.azurerm_key_vault_secret.sql_server_password.value
}

resource "azurerm_mssql_database" "sql_db" {
  name         = "webvite-sqldb-${var.environment_suffix}"
  server_id    = azurerm_mssql_server.sql_server.id
  collation    = "SQL_Latin1_General_CP1_CI_AS"
  license_type = "LicenseIncluded"
  max_size_gb  = 2
  sku_name     = "Basic"
}