param($EnvironmentSuffix)

.\deploy-azure-set-subscription.ps1 $EnvironmentSuffix

Set-Location ../Terraform

$environmentSuffixVarFlag = '-var=environment_suffix=' + $EnvironmentSuffix
$autoApproveFlag = '-auto-approve'
$noInputFlag = '-input=false'

######### RUN TERRAFORM APPLY #########
Write-Output "Starting terraform apply"
Write-Output "terraform apply $environmentSuffixVarFlag $autoApproveFlag $noInputFlag"
terraform apply $environmentSuffixVarFlag $autoApproveFlag $noInputFlag
if($LASTEXITCODE -ne 0){
    throw
}

Set-Location ../Cicd

Write-Output "Completed terraform apply"