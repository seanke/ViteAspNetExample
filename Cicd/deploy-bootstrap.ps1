param($EnvironmentSuffix)

if (-not $EnvironmentSuffix)
{
    throw "environment_suffix not provided"
}

.\deploy-azure-set-subscription.ps1 $EnvironmentSuffix

######### LOAD CONFIG FILE #########
$config = Get-Content "config.$EnvironmentSuffix.json" | Out-String | ConvertFrom-Json

$SubscriptionId = $config.azureSubscriptionId
$Location = $config.azureLocation
$AppName = $config.appName

$resourceGroupName = "$AppName-rg-$EnvironmentSuffix"
$storageAccountName = $AppName + 'storage' + $EnvironmentSuffix

######### CREATE RESOURCE GROUP #########
Write-Output "Setting up resource group"

az group create `
    --location $Location `
    --name $resourceGroupName `
    --subscription $SubscriptionId
if($LASTEXITCODE -ne 0){
    throw "Error creating resource group"
}

######### CREATE KEY VAULT #########
$createKeyVault = {
    param ($KeyVaultName, $KeyVaultResourceGroupName, $KeyVaultLocation, $KeyVaultSubscriptionId)

    az keyvault show --name $KeyVaultName --resource-group $KeyVaultResourceGroupName --subscription $KeyVaultSubscriptionId
    
    if($LASTEXITCODE -ne 0){
        Write-Output "Key vault does not exist, creating"
        
        az keyvault create `
        --name $KeyVaultName `
        --resource-group $KeyVaultResourceGroupName `
        --location $KeyVaultLocation `
        --subscription $KeyVaultSubscriptionId
        
        if($LASTEXITCODE -ne 0){
            throw "Error creating key vault"
        }
    }

    $account = az account show | ConvertFrom-Json
    Write-Host $account
    Write-Host "account id = $($account.user.name)"
    if($LASTEXITCODE -ne 0){
        throw "Error getting account details"
    }
    
    if($account.user.type -eq "user"){
        $accountObjectId = az ad signed-in-user show --query id --output tsv
    }
    else{
        $accountObjectId = "$($account.user.name)"
    }
    
    Write-Output "Adding access policy to key vault"
    #add access
    az keyvault set-policy `
        --name $KeyVaultName `
        --secret-permissions get set list `
        --key-permissions `
        --storage-permissions `
        --certificate-permissions `
        --object-id $accountObjectId
    if($LASTEXITCODE -ne 0){
        throw "Error creating key vault access policy"
    }
    
    Write-Output "Checking for sql server password secret"
    #sql-server-password
    $sqlServerPasswordSecret = az keyvault secret list-versions `
        --name sql-server-password `
        --vault-name $KeyVaultName `
        --maxresults 1 `
        --output tsv
    if($LASTEXITCODE -ne 0){
        throw "Error get key vault sql server secret"
    }
    
    if($sqlServerPasswordSecret.count -eq 0){
        Write-Output "Creating sql server password secret"
        Add-Type -AssemblyName System.Web
        $sqlServerPassword = [System.Web.Security.Membership]::GeneratePassword(50, 0)
        az keyvault secret set `
            --vault-name $KeyVaultName `
            --name sql-server-password `
            --value $sqlServerPassword
        if($LASTEXITCODE -ne 0){
            throw "Error creating key vault sql server secret"
        }
    }
}
$createKeyVaultResult  = Start-Job `
    -Name "CreateKeyVault" `
    -ScriptBlock $createKeyVault `
    -ArgumentList @("$AppName-kv-$EnvironmentSuffix", $ResourceGroupName, $Location, $SubscriptionId)

$createStorageAccount = {
    param ($StorageName, $StorageResourceGroupName, $StorageSubscriptionId)

    function ThrowIfFailed($error)
    {
        if($LASTEXITCODE -ne 0){
            throw $error
        }
    }
    
    ######### CREATE STORAGE ACCOUNT #########
    Write-Output "Setting up storage account"
    az storage account create `
        --name $StorageName `
        --resource-group $StorageResourceGroupName `
        --subscription $StorageSubscriptionId
    ThrowIfFailed "Error creating storage account"

    Write-Output "Setting up storage container"
    $output = az storage account keys list `
    --account-name $StorageName `
    --resource-group $StorageResourceGroupName `
    --subscription $StorageSubscriptionId | ConvertFrom-Json -ErrorAction Stop
    ThrowIfFailed "Error getting storage account keys"

    $key = $output[0].Value

    Write-Output "Creating storage container"
    az storage container create `
        --name terraform `
        --account-name $StorageName `
        --subscription $StorageSubscriptionId `
        --account-key $key --output json
    ThrowIfFailed "Error creating storage container"
}
$createStorageResult = Start-Job `
    -Name "CreateStorageAccount" `
    -ScriptBlock $createStorageAccount `
    -ArgumentList @($storageAccountName, $ResourceGroupName, $SubscriptionId)

$createStorageResult | Wait-Job
$createKeyVaultResult | Wait-Job

if($createStorageResult.State -ne "Completed")
{
    Write-Output "################### Storage Account Result ###################"
    $createStorageResult | Receive-Job
    throw "Error creating storage account"
}

if($createKeyVaultResult.State -ne "Completed")
{
    Write-Output "################### Key Vault Result ###################"
    $createKeyVaultResult | Receive-Job
}
