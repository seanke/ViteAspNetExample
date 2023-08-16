param($EnvironmentSuffix)

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDirectory


Write-Host "Starting deploy-bootstrap.ps1" -ForegroundColor Cyan
./deploy-bootstrap.ps1 $EnvironmentSuffix
Write-Host "Completed deploy-bootstrap.ps1" -ForegroundColor Cyan

Write-Host "Starting deploy-terraform-init.ps1" -ForegroundColor Cyan
./deploy-terraform-init.ps1 $EnvironmentSuffix
Write-Host "Completed deploy-terraform-init.ps1" -ForegroundColor Cyan

Write-Host "Starting deploy-terraform-apply.ps1" -ForegroundColor Cyan
./deploy-terraform-apply.ps1 $EnvironmentSuffix
Write-Host "Completed deploy-terraform-apply.ps1" -ForegroundColor Cyan

Write-Host "Start deploy-web-app.ps1" -ForegroundColor Cyan
./deploy-web-app.ps1 $EnvironmentSuffix
Write-Host "Completed deploy-web-app.ps1" -ForegroundColor Cyan