param(
    [Parameter(Mandatory=$true)]
    $EnvironmentSuffix
)

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDirectory


Write-Host "Starting build.ps1" -ForegroundColor Green
./build.ps1 $EnvironmentSuffix
Write-Host "Completed build.ps1" -ForegroundColor Green

Write-Host "Starting deploy.ps1" -ForegroundColor Green
./deploy.ps1 $EnvironmentSuffix
Write-Host "Completed deploy.ps1" -ForegroundColor Green