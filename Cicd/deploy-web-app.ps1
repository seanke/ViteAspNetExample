param($EnvironmentSuffix)

.\deploy-azure-set-subscription.ps1 $EnvironmentSuffix

$config = Get-Content "config.$EnvironmentSuffix.json" | Out-String | ConvertFrom-Json

$resourceGroupName = $config.appName + '-rg-' + $EnvironmentSuffix
$webAppName = $config.appName + '-wa-' + $EnvironmentSuffix
$zipFilePath = './../.build/WebVite.zip'

az webapp deploy `
    --resource-group $resourceGroupName `
    --name $webAppName `
    --src-path $zipFilePath `
    --type zip `
    --only-show-errors

az webapp restart `
    --name $webAppName `
    --resource-group $resourceGroupName
