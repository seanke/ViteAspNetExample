variable "environment_suffix" {}

data "azurerm_resource_group" "resource_group"{
  name = "webvite-rg-${var.environment_suffix}"
}

resource "azurerm_service_plan" "service_plan" {
  name                = "webvite-asp-${var.environment_suffix}"
  resource_group_name = data.azurerm_resource_group.resource_group.name
  location            = data.azurerm_resource_group.resource_group.location
  sku_name            = "F1"
  os_type             = "Windows"
}

resource "azurerm_windows_web_app" "web_app" {
  name                = "webvite-wa-${var.environment_suffix}"
  resource_group_name = data.azurerm_resource_group.resource_group.name
  location            = data.azurerm_resource_group.resource_group.location
  service_plan_id     = azurerm_service_plan.service_plan.id

  site_config {
    always_on = false
    virtual_application {
      physical_path = "site\\wwwroot"
      preload       = false
      virtual_path  = "/"
    }
  }
}