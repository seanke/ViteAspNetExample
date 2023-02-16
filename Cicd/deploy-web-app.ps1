param($EnvironmentSuffix)

.\deploy-azure-set-subscription.ps1 $EnvironmentSuffix

$config = Get-Content "config.$EnvironmentSuffix.json" | Out-String | ConvertFrom-Json

$resourceGroupName = $config.appName + '-rg-' + $EnvironmentSuffix
$webAppName = $config.appName + '-wa-' + $EnvironmentSuffix
$zipFilePath = '../.build/WebApp.zip'

az webapp deploy `
    --resource-group $resourceGroupName `
    --name $webAppName `
    --src-path $zipFilePath `
    --type zip `
    --only-show-errors
if($LASTEXITCODE -ne 0){
    throw
}

az webapp restart `
    --name $webAppName `
    --resource-group $resourceGroupName
if($LASTEXITCODE -ne 0){
    throw
}
