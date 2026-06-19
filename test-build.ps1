# Simple test script to verify the build system works
param(
    [switch]$SkipUpload = $false,
    [switch]$Local = $false
)

Write-Host "=== TSB Build Test ===" -ForegroundColor Green
Write-Host "Testing individual scripts..." -ForegroundColor Yellow

$ErrorActionPreference = "Stop"

try {
    # Test 1: Environment Setup
    Write-Host "`n1. Testing environment setup..." -ForegroundColor Cyan
    & ".\scripts\setup-environment.ps1" -Local:$Local
    Write-Host "✓ Environment setup completed" -ForegroundColor Green
    
    # Test 2: Build Application  
    Write-Host "`n2. Testing application build..." -ForegroundColor Cyan
    & ".\scripts\build-application.ps1" -Configuration "Release" -Platforms @("x64") -SkipTests -Local:$Local
    Write-Host "✓ Application build completed" -ForegroundColor Green
    
    # Test 3: Collect Artifacts
    Write-Host "`n3. Testing artifact collection..." -ForegroundColor Cyan
    & ".\scripts\collect-artifacts.ps1" -Configuration "Release" -Platforms @("x64") -Local:$Local
    Write-Host "✓ Artifact collection completed" -ForegroundColor Green
    
    Write-Host "`n=== Build Test Successful ===" -ForegroundColor Green
    
} catch {
    Write-Host "`n=== Build Test Failed ===" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}



