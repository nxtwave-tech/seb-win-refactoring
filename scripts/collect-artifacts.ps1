# Collect TSB Build Artifacts
# Gathers all distributable files into a structured artifacts directory

param(
    [string]$Configuration = "Release",
    [string[]]$Platforms = @("x64", "x86"),
    [string]$OutputDir = "artifacts",
    [switch]$Local = $false
)

Write-Host "=== TSB Artifact Collection ===" -ForegroundColor Green

# Detect environment
$IsCodeBuild = $env:CODEBUILD_BUILD_ID -ne $null
$IsLocal = $Local -or (-not $IsCodeBuild)

Write-Host "Environment: $(if($IsCodeBuild) {'AWS CodeBuild'} else {'Local Development'})" -ForegroundColor Yellow
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow

# Set common variables
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Get paths
$SolutionDir = if ($env:CODEBUILD_SRC_DIR) { $env:CODEBUILD_SRC_DIR } else { (Get-Location).Path }
$ArtifactsDir = Join-Path $SolutionDir $OutputDir

Write-Host "Solution Directory: $SolutionDir" -ForegroundColor Cyan
Write-Host "Artifacts Directory: $ArtifactsDir" -ForegroundColor Cyan

# Create artifacts directory structure
Write-Host "`nCreating artifacts directory structure..." -ForegroundColor Yellow

if (Test-Path $ArtifactsDir) {
    Remove-Item $ArtifactsDir -Recurse -Force
}

New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null

# Create platform-specific subdirectories
foreach ($platform in $Platforms) {
    New-Item -ItemType Directory -Path (Join-Path $ArtifactsDir $platform) -Force | Out-Null
}

Write-Host "Artifacts directory structure created" -ForegroundColor Green

# Function to copy file with validation
function Copy-ArtifactFile($sourcePath, $destinationPath, $description) {
    if (Test-Path $sourcePath) {
        $sourceSize = [math]::Round((Get-Item $sourcePath).Length / 1MB, 2)
        Copy-Item $sourcePath $destinationPath -Force
        Write-Host "  ✓ $description ($sourceSize MB)" -ForegroundColor Green
        return $true
    } else {
        Write-Host "  ✗ $description (NOT FOUND: $sourcePath)" -ForegroundColor Red
        return $false
    }
}

# Function to copy directory with validation
function Copy-ArtifactDirectory($sourcePath, $destinationPath, $description) {
    if (Test-Path $sourcePath) {
        Copy-Item $sourcePath $destinationPath -Recurse -Force
        $fileCount = (Get-ChildItem $destinationPath -Recurse -File).Count
        Write-Host "  ✓ $description ($fileCount files)" -ForegroundColor Green
        return $true
    } else {
        Write-Host "  ✗ $description (NOT FOUND: $sourcePath)" -ForegroundColor Red
        return $false
    }
}

# Collect artifacts for each platform
$collectionSummary = @{}

foreach ($platform in $Platforms) {
    Write-Host "`nCollecting artifacts for platform: $platform" -ForegroundColor Yellow
    
    $platformDir = Join-Path $ArtifactsDir $platform
    $platformSummary = @{
        Application = $false
        MSI = $false
        Bundle = $false
        Dependencies = $false
    }
    
    # 1. Main Application (SafeExamBrowser.exe)
    Write-Host "  Collecting main application..." -ForegroundColor Cyan
    $appSourceDir = Join-Path $SolutionDir "SafeExamBrowser.Runtime\bin\$platform\$Configuration"
    $appDestDir = Join-Path $platformDir "Application"
    
    if (Test-Path $appSourceDir) {
        New-Item -ItemType Directory -Path $appDestDir -Force | Out-Null
        
        # Copy main executable
        $appExePath = Join-Path $appSourceDir "SafeExamBrowser.exe"
        $platformSummary.Application = Copy-ArtifactFile $appExePath (Join-Path $appDestDir "SafeExamBrowser.exe") "SafeExamBrowser.exe"
        
        # Copy all dependencies (DLLs, config files, etc.)
        $dependencyFiles = Get-ChildItem $appSourceDir -File | Where-Object { $_.Extension -in @('.dll', '.config', '.pdb') }
        foreach ($file in $dependencyFiles) {
            Copy-Item $file.FullName (Join-Path $appDestDir $file.Name) -Force
        }
        
        if ($dependencyFiles.Count -gt 0) {
            Write-Host "  ✓ Dependencies ($($dependencyFiles.Count) files)" -ForegroundColor Green
            $platformSummary.Dependencies = $true
        }
        
        # Copy any subdirectories (like localization, plugins, etc.)
        $subDirs = Get-ChildItem $appSourceDir -Directory
        foreach ($subDir in $subDirs) {
            Copy-ArtifactDirectory $subDir.FullName (Join-Path $appDestDir $subDir.Name) "Directory: $($subDir.Name)"
        }
    }
    
    # 2. MSI Installer
    Write-Host "  Collecting MSI installer..." -ForegroundColor Cyan
    $msiPath = Join-Path $SolutionDir "Setup\bin\$platform\$Configuration\TSB.msi"
    $platformSummary.MSI = Copy-ArtifactFile $msiPath (Join-Path $platformDir "TSB.msi") "TSB.msi"
    
    # 3. EXE Bundle
    Write-Host "  Collecting EXE bundle..." -ForegroundColor Cyan
    $bundlePath = Join-Path $SolutionDir "SetupBundle\bin\$platform\$Configuration\TSB.exe"
    $platformSummary.Bundle = Copy-ArtifactFile $bundlePath (Join-Path $platformDir "TSB.exe") "TSB.exe"
    
    # 4. Additional components (Service, Client, etc.)
    Write-Host "  Collecting additional components..." -ForegroundColor Cyan
    
    # Service executable
    $serviceSourceDir = Join-Path $SolutionDir "SafeExamBrowser.Service\bin\$platform\$Configuration"
    if (Test-Path $serviceSourceDir) {
        $serviceDestDir = Join-Path $platformDir "Service"
        Copy-ArtifactDirectory $serviceSourceDir $serviceDestDir "Service Components"
    }
    
    # Client executable  
    $clientSourceDir = Join-Path $SolutionDir "SafeExamBrowser.Client\bin\$platform\$Configuration"
    if (Test-Path $clientSourceDir) {
        $clientDestDir = Join-Path $platformDir "Client"
        Copy-ArtifactDirectory $clientSourceDir $clientDestDir "Client Components"
    }
    
    # Configuration Tool
    $configToolSourceDir = Join-Path $SolutionDir "SebWindowsConfig\bin\$platform\$Configuration"
    if (Test-Path $configToolSourceDir) {
        $configToolDestDir = Join-Path $platformDir "ConfigTool"
        Copy-ArtifactDirectory $configToolSourceDir $configToolDestDir "Configuration Tool"
    }
    
    $collectionSummary[$platform] = $platformSummary
}

# 5. Copy common files (documentation, licenses, etc.)
Write-Host "`nCollecting common files..." -ForegroundColor Yellow

$commonFiles = @(
    @{ Source = "LICENSE.txt"; Description = "License" },
    @{ Source = "BUILD_GUIDE.md"; Description = "Build Guide" },
    @{ Source = "README.md"; Description = "README" }
)

foreach ($file in $commonFiles) {
    $sourcePath = Join-Path $SolutionDir $file.Source
    if (Test-Path $sourcePath) {
        Copy-Item $sourcePath (Join-Path $ArtifactsDir $file.Source) -Force
        Write-Host "  ✓ $($file.Description)" -ForegroundColor Green
    }
}

# 6. Copy build information
Write-Host "`nCopying build information..." -ForegroundColor Yellow

$buildInfoPath = Join-Path $SolutionDir "build-info.json"
if (Test-Path $buildInfoPath) {
    Copy-Item $buildInfoPath (Join-Path $ArtifactsDir "build-info.json") -Force
    Write-Host "  ✓ Build Information" -ForegroundColor Green
}

# 7. Create deployment-ready structure (for S3 upload)
Write-Host "`nCreating deployment-ready structure..." -ForegroundColor Yellow

$deployDir = Join-Path $ArtifactsDir "deploy"
New-Item -ItemType Directory -Path $deployDir -Force | Out-Null

# Copy primary platform files to root (typically x64)
$primaryPlatform = "x64"
if ($primaryPlatform -in $Platforms) {
    $primaryDir = Join-Path $ArtifactsDir $primaryPlatform
    
    # Main installer files (without platform suffix)
    Copy-ArtifactFile (Join-Path $primaryDir "TSB.msi") (Join-Path $deployDir "TSB.msi") "Primary MSI Installer"
    Copy-ArtifactFile (Join-Path $primaryDir "TSB.exe") (Join-Path $deployDir "TSB.exe") "Primary EXE Bundle"
    
    # Application directory
    $appDir = Join-Path $primaryDir "Application"
    if (Test-Path $appDir) {
        Copy-ArtifactDirectory $appDir (Join-Path $deployDir "Application") "Primary Application"
    }
}

# Copy platform-specific files with suffixes
foreach ($platform in $Platforms) {
    $platformDir = Join-Path $ArtifactsDir $platform
    
    if ($platform -ne $primaryPlatform) {
        # Add platform suffix for non-primary platforms
        Copy-ArtifactFile (Join-Path $platformDir "TSB.msi") (Join-Path $deployDir "TSB-$platform.msi") "$platform MSI Installer"
        Copy-ArtifactFile (Join-Path $platformDir "TSB.exe") (Join-Path $deployDir "TSB-$platform.exe") "$platform EXE Bundle"
        
        $appDir = Join-Path $platformDir "Application"
        if (Test-Path $appDir) {
            $appExe = Join-Path $appDir "SafeExamBrowser.exe"
            if (Test-Path $appExe) {
                Copy-ArtifactFile $appExe (Join-Path $deployDir "SafeExamBrowser-$platform.exe") "$platform Application"
            }
        }
    }
}

# Copy common files to deploy directory
foreach ($file in $commonFiles) {
    $sourcePath = Join-Path $ArtifactsDir $file.Source
    if (Test-Path $sourcePath) {
        Copy-Item $sourcePath (Join-Path $deployDir $file.Source) -Force
    }
}

Copy-Item (Join-Path $ArtifactsDir "build-info.json") (Join-Path $deployDir "build-info.json") -Force

# 8. Generate collection summary
Write-Host "`n=== Artifact Collection Summary ===" -ForegroundColor Green

$totalSize = 0
$artifactFiles = Get-ChildItem $ArtifactsDir -Recurse -File
foreach ($file in $artifactFiles) {
    $totalSize += $file.Length
}

Write-Host "Total artifacts size: $([math]::Round($totalSize / 1MB, 2)) MB" -ForegroundColor Yellow
Write-Host "Total files collected: $($artifactFiles.Count)" -ForegroundColor Yellow

foreach ($platform in $Platforms) {
    Write-Host "`nPlatform: $platform" -ForegroundColor Yellow
    $summary = $collectionSummary[$platform]
    
    Write-Host "  Application: $(if($summary.Application) {'✓'} else {'✗'})" -ForegroundColor $(if($summary.Application) {'Green'} else {'Red'})
    Write-Host "  MSI Installer: $(if($summary.MSI) {'✓'} else {'✗'})" -ForegroundColor $(if($summary.MSI) {'Green'} else {'Red'})
    Write-Host "  EXE Bundle: $(if($summary.Bundle) {'✓'} else {'✗'})" -ForegroundColor $(if($summary.Bundle) {'Green'} else {'Red'})
    Write-Host "  Dependencies: $(if($summary.Dependencies) {'✓'} else {'✗'})" -ForegroundColor $(if($summary.Dependencies) {'Green'} else {'Red'})
}

# Check deployment directory
$deployFiles = Get-ChildItem $deployDir -File
Write-Host "`nDeployment-ready files:" -ForegroundColor Yellow
foreach ($file in $deployFiles) {
    $fileSize = [math]::Round($file.Length / 1MB, 2)
    Write-Host "  $($file.Name) ($fileSize MB)" -ForegroundColor Cyan
}

Write-Host "`n=== Artifact Collection Complete ===" -ForegroundColor Green
Write-Host "Artifacts directory: $ArtifactsDir" -ForegroundColor Cyan
Write-Host "Deployment directory: $deployDir" -ForegroundColor Cyan

# Export artifact info for upload script
$artifactInfo = @{
    ArtifactsDir = $ArtifactsDir
    DeployDir = $deployDir
    Platforms = $Platforms
    TotalSize = $totalSize
    FileCount = $artifactFiles.Count
    Summary = $collectionSummary
}

$artifactInfoPath = Join-Path $SolutionDir "artifact-info.json"
$artifactInfo | ConvertTo-Json -Depth 3 | Out-File -FilePath $artifactInfoPath -Encoding UTF8

Write-Host "Artifact information saved to: $artifactInfoPath" -ForegroundColor Cyan



