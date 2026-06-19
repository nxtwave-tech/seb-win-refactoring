# Setup Environment for TSB Build
# Works in both local development and AWS CodeBuild environments

param(
    [switch]$Local = $false
)

Write-Host "=== TSB Build Environment Setup ===" -ForegroundColor Green

# Detect environment
$IsCodeBuild = $env:CODEBUILD_BUILD_ID -ne $null
$IsLocal = $Local -or (-not $IsCodeBuild)

Write-Host "Environment: $(if($IsCodeBuild) {'AWS CodeBuild'} else {'Local Development'})" -ForegroundColor Yellow

# Set common variables
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Function to check if command exists
function Test-Command($command) {
    try {
        Get-Command $command -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

# Function to download and install from URL
function Install-FromUrl($name, $url, $installer, $args = @()) {
    Write-Host "Installing $name..." -ForegroundColor Cyan
    
    $tempFile = Join-Path $env:TEMP $installer
    
    try {
        Invoke-WebRequest -Uri $url -OutFile $tempFile -UseBasicParsing
        Write-Host "Downloaded $installer" -ForegroundColor Green
        
        $process = Start-Process -FilePath $tempFile -ArgumentList $args -Wait -PassThru -NoNewWindow
        
        if ($process.ExitCode -eq 0) {
            Write-Host "$name installed successfully" -ForegroundColor Green
        } else {
            throw "$name installation failed with exit code: $($process.ExitCode)"
        }
    } finally {
        if (Test-Path $tempFile) {
            Remove-Item $tempFile -Force
        }
    }
}

# 1. Install Visual Studio Build Tools 2022 (if not exists)
Write-Host "`n1. Checking Visual Studio Build Tools 2022..." -ForegroundColor Yellow

$msbuildPaths = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
)

$msbuildPath = $msbuildPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $msbuildPath) {
    Write-Host "MSBuild not found. Installing Visual Studio Build Tools 2022..." -ForegroundColor Yellow
    
    $vsUrl = "https://aka.ms/vs/17/release/vs_buildtools.exe"
    $vsArgs = @(
        "--quiet", 
        "--wait", 
        "--add", "Microsoft.VisualStudio.Workload.MSBuildTools",
        "--add", "Microsoft.VisualStudio.Workload.NetCoreBuildTools",
        "--add", "Microsoft.Net.Component.4.8.SDK",
        "--add", "Microsoft.Net.Component.4.8.TargetingPack"
    )
    
    Install-FromUrl "Visual Studio Build Tools 2022" $vsUrl "vs_buildtools.exe" $vsArgs
    
    # Re-check for MSBuild
    $msbuildPath = $msbuildPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    
    if (-not $msbuildPath) {
        throw "MSBuild installation failed or not found after installation"
    }
}

Write-Host "MSBuild found: $msbuildPath" -ForegroundColor Green
$env:MSBUILD_PATH = $msbuildPath

# 2. Install WiX Toolset v3.14 (if not exists)
Write-Host "`n2. Checking WiX Toolset v3.14..." -ForegroundColor Yellow

$wixPaths = @(
    "${env:ProgramFiles(x86)}\WiX Toolset v3.14",
    "${env:ProgramFiles}\WiX Toolset v3.14"
)

$wixPath = $wixPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $wixPath) {
    Write-Host "WiX Toolset not found. Installing WiX Toolset v3.14..." -ForegroundColor Yellow
    
    $wixUrl = "https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314.exe"
    $wixArgs = @("/quiet", "/norestart")
    
    Install-FromUrl "WiX Toolset v3.14" $wixUrl "wix314.exe" $wixArgs
    
    # Re-check for WiX
    $wixPath = $wixPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    
    if (-not $wixPath) {
        throw "WiX Toolset installation failed or not found after installation"
    }
}

Write-Host "WiX Toolset found: $wixPath" -ForegroundColor Green
$env:WIX = $wixPath

# 3. Install .NET Framework 4.8 Developer Pack (if not exists)
Write-Host "`n3. Checking .NET Framework 4.8..." -ForegroundColor Yellow

$dotnetRegPath = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
$dotnetVersion = $null

if (Test-Path $dotnetRegPath) {
    $dotnetVersion = (Get-ItemProperty $dotnetRegPath -ErrorAction SilentlyContinue).Version
}

if (-not $dotnetVersion -or $dotnetVersion -lt "4.8") {
    Write-Host ".NET Framework 4.8 not found. Installing .NET Framework 4.8 Developer Pack..." -ForegroundColor Yellow
    
    $dotnetUrl = "https://download.microsoft.com/download/2/E/6/2E61CFA4-993B-4DD4-91DA-3737CD5CD6E3/ndp48-devpack-enu.exe"
    $dotnetArgs = @("/quiet", "/norestart")
    
    Install-FromUrl ".NET Framework 4.8 Developer Pack" $dotnetUrl "ndp48-devpack-enu.exe" $dotnetArgs
} else {
    Write-Host ".NET Framework 4.8 found: $dotnetVersion" -ForegroundColor Green
}

# 4. Set up NuGet (ensure latest version)
Write-Host "`n4. Setting up NuGet..." -ForegroundColor Yellow

if (-not (Test-Command "nuget")) {
    Write-Host "Installing NuGet CLI..." -ForegroundColor Yellow
    
    $nugetUrl = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"
    $nugetPath = Join-Path $env:TEMP "nuget.exe"
    
    Invoke-WebRequest -Uri $nugetUrl -OutFile $nugetPath -UseBasicParsing
    
    # Add to PATH for current session
    $env:PATH = "$env:TEMP;$env:PATH"
    
    Write-Host "NuGet CLI installed" -ForegroundColor Green
} else {
    Write-Host "NuGet CLI already available" -ForegroundColor Green
}

# 5. Verify installations
Write-Host "`n=== Verification ===" -ForegroundColor Green

Write-Host "MSBuild: $env:MSBUILD_PATH" -ForegroundColor Cyan
Write-Host "WiX: $env:WIX" -ForegroundColor Cyan

# Test MSBuild
& "$env:MSBUILD_PATH" /version | Select-Object -First 3 | ForEach-Object { Write-Host $_ -ForegroundColor Gray }

Write-Host "`n=== Environment Setup Complete ===" -ForegroundColor Green
Write-Host "Ready to build TSB application!" -ForegroundColor Yellow

# Export environment variables for subsequent scripts
if ($IsCodeBuild) {
    # In CodeBuild, set environment variables for next phases
    Write-Host "`nExporting environment variables for CodeBuild..." -ForegroundColor Yellow
    Write-Host "##[set-env name=MSBUILD_PATH]$env:MSBUILD_PATH"
    Write-Host "##[set-env name=WIX]$env:WIX"
}



