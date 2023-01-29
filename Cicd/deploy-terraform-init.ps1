param($EnvironmentSuffix)

.\deploy-azure-set-subscription.ps1 $EnvironmentSuffix

######### LOAD CONFIG FILE #########
$config = Get-Content "config.$EnvironmentSuffix.json" | Out-String | ConvertFrom-Json

$TerraformStateResourceGroupName = $config.appName + '-rg-' + $EnvironmentSuffix
$TerraformStateStorageAccountName = $config.appName + 'storage' + $EnvironmentSuffix
$TerraformContainerName = 'terraform'
$TerraformStateKey = 'terraform.tfstate'

Set-Location ../Terraform

######### CLEAR TERRAFORM CACHE #########
Set-Location ../Terraform
if (Test-Path .terraform/terraform.tfstate)
{
    Remove-Item .terraform/terraform.tfstate
    Write-Output "Removed .terraform/terraform.tfstate"
}

######### SETUP TERRAFORM BACKEND CONFIG #########
$initResourceGroupCommand = '-backend-config="resource_group_name=' + $TerraformStateResourceGroupName + '"'
$initStorageAccountCommand = '-backend-config="storage_account_name=' + $TerraformStateStorageAccountName + '"'
$initContainerCommand = '-backend-config="container_name=' + $TerraformContainerName + '"'
$initStateKeyCommand = '-backend-config="key=' + $TerraformStateKey + '"'

######### RUN TERRAFORM INIT #########
Write-Output "Starting terraform init"
terraform init $initResourceGroupCommand $initStorageAccountCommand $initContainerCommand $initStateKeyCommand -upgrade

if($LASTEXITCODE -ne 0){
    throw
}

Set-Location ../Cicd

Write-Output "Completed terraform init"