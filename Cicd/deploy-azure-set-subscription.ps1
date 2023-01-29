param($EnvironmentSuffix)

######### LOAD CONFIG FILE #########
$config = Get-Content "config.$EnvironmentSuffix.json" | Out-String | ConvertFrom-Json

$SubscriptionId = $config.azureSubscriptionId

######### SET SUBSCRIPTION #########
az account set --subscription $SubscriptionId
$output = az account show --subscription $SubscriptionId --output json | ConvertFrom-Json -ErrorAction Stop
if (-not $output.isDefault)
{
    throw "Error setting subscription"
}

Write-Output "Environment: $EnvironmentSuffix | Subscription Name:" $output.name