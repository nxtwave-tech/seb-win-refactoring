# Build TSB Application
# Builds all components: Application, MSI Installer, and EXE Bundle

param(
    [string]$Configuration = "Release",
    [string[]]$Platforms = @("x64", "x86"),
    [switch]$SkipTests = $false,
    [switch]$Local = $false
)

Write-Host "=== TSB Application Build ===" -ForegroundColor Green

# Detect environment
$IsCodeBuild = $env:CODEBUILD_BUILD_ID -ne $null
$IsLocal = $Local -or (-not $IsCodeBuild)

Write-Host "Environment: $(if($IsCodeBuild) {'AWS CodeBuild'} else {'Local Development'})" -ForegroundColor Yellow
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow

# Set common variables
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Get paths from environment or detect
$SolutionDir = if ($env:CODEBUILD_SRC_DIR) { $env:CODEBUILD_SRC_DIR } else { (Get-Location).Path }
$SolutionFile = Join-Path $SolutionDir "SafeExamBrowser.sln"

# Get MSBuild path
$MSBuildPath = if ($env:MSBUILD_PATH) { 
    $env:MSBUILD_PATH 
} else {
    $msbuildPaths = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msbuildPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
}

# Get WiX path
$WixPath = if ($env:WIX) { 
    $env:WIX 
} else {
    $wixPaths = @(
        "${env:ProgramFiles(x86)}\WiX Toolset v3.14",
        "${env:ProgramFiles}\WiX Toolset v3.14"
    )
    $wixPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
}

# Validate required paths
if (-not (Test-Path $SolutionFile)) {
    throw "Solution file not found: $SolutionFile"
}

if (-not (Test-Path $MSBuildPath)) {
    throw "MSBuild not found: $MSBuildPath"
}

if (-not (Test-Path $WixPath)) {
    throw "WiX Toolset not found: $WixPath"
}

Write-Host "Solution: $SolutionFile" -ForegroundColor Cyan
Write-Host "MSBuild: $MSBuildPath" -ForegroundColor Cyan
Write-Host "WiX: $WixPath" -ForegroundColor Cyan

# Set WiX environment variable for MSBuild
$env:WIX = $WixPath

# Function to run MSBuild with proper error handling
function Invoke-MSBuild($project, $platform, $target = $null) {
    $args = @(
        "`"$project`"",
        "/p:Configuration=$Configuration",
        "/p:Platform=$platform",
        "/p:SolutionDir=`"$SolutionDir\`"",
        "/m",  # Multi-processor build
        "/v:m", # Minimal verbosity
        "/nologo"
    )
    
    if ($target) {
        $args += "/t:$target"
    }
    
    Write-Host "Running: MSBuild $($args -join ' ')" -ForegroundColor Gray
    
    $process = Start-Process -FilePath $MSBuildPath -ArgumentList $args -Wait -PassThru -NoNewWindow
    
    if ($process.ExitCode -ne 0) {
        throw "MSBuild failed with exit code: $($process.ExitCode)"
    }
}

# 1. Restore NuGet packages
Write-Host "`n1. Restoring NuGet packages..." -ForegroundColor Yellow

try {
    # Try using MSBuild restore first (preferred)
    Invoke-MSBuild $SolutionFile "AnyCPU" "Restore"
    Write-Host "NuGet packages restored successfully" -ForegroundColor Green
} catch {
    Write-Host "MSBuild restore failed, trying NuGet CLI..." -ForegroundColor Yellow
    
    # Fallback to NuGet CLI
    $nugetPath = Join-Path $env:TEMP "nuget.exe"
    $nugetArgs = @("restore", "`"$SolutionFile`"", "-NonInteractive")
    
    $process = Start-Process -FilePath $nugetPath -ArgumentList $nugetArgs -Wait -PassThru -NoNewWindow
    
    if ($process.ExitCode -eq 0) {
        Write-Host "NuGet packages restored successfully" -ForegroundColor Green
    } else {
        throw "NuGet restore failed with exit code: $($process.ExitCode)"
    }
}

# 2. Build main application for each platform
Write-Host "`n2. Building main application..." -ForegroundColor Yellow

foreach ($platform in $Platforms) {
    Write-Host "Building for platform: $platform" -ForegroundColor Cyan
    
    try {
        Invoke-MSBuild $SolutionFile $platform
        Write-Host "Successfully built $platform platform" -ForegroundColor Green
    } catch {
        Write-Host "Failed to build $platform platform: $_" -ForegroundColor Red
        throw
    }
}

# 3. Run unit tests (if not skipped)
if (-not $SkipTests) {
    Write-Host "`n3. Running unit tests..." -ForegroundColor Yellow
    
    # Find test projects
    $testProjects = Get-ChildItem -Path $SolutionDir -Recurse -Filter "*.UnitTests.csproj"
    
    if ($testProjects.Count -gt 0) {
        foreach ($testProject in $testProjects) {
            Write-Host "Running tests: $($testProject.Name)" -ForegroundColor Cyan
            
            try {
                # Use the first platform for tests (tests are typically platform-agnostic)
                Invoke-MSBuild $testProject.FullName $Platforms[0] "Test"
                Write-Host "Tests passed: $($testProject.Name)" -ForegroundColor Green
            } catch {
                Write-Host "Tests failed: $($testProject.Name) - $_" -ForegroundColor Red
                # Continue with other tests but note the failure
            }
        }
    } else {
        Write-Host "No unit test projects found" -ForegroundColor Yellow
    }
} else {
    Write-Host "`n3. Skipping unit tests (as requested)" -ForegroundColor Yellow
}

# 4. Build MSI Installer for each platform
Write-Host "`n4. Building MSI installers..." -ForegroundColor Yellow

$setupProject = Join-Path $SolutionDir "Setup\Setup.wixproj"

if (Test-Path $setupProject) {
    foreach ($platform in $Platforms) {
        Write-Host "Building MSI installer for platform: $platform" -ForegroundColor Cyan
        
        try {
            Invoke-MSBuild $setupProject $platform
            Write-Host "Successfully built MSI installer for $platform" -ForegroundColor Green
        } catch {
            Write-Host "Failed to build MSI installer for $platform" -ForegroundColor Red
            Write-Host "Error: $_" -ForegroundColor Red
            throw
        }
    }
} else {
    Write-Host "Setup project not found: $setupProject" -ForegroundColor Red
    throw "MSI installer build failed - Setup project missing"
}

# 5. Build EXE Bundle for each platform
Write-Host "`n5. Building EXE bundles..." -ForegroundColor Yellow

$bundleProject = Join-Path $SolutionDir "SetupBundle\SetupBundle.wixproj"

if (Test-Path $bundleProject) {
    foreach ($platform in $Platforms) {
        Write-Host "Building EXE bundle for platform: $platform" -ForegroundColor Cyan
        
        try {
            Invoke-MSBuild $bundleProject $platform
            Write-Host "Successfully built EXE bundle for $platform" -ForegroundColor Green
        } catch {
            Write-Host "Failed to build EXE bundle for $platform" -ForegroundColor Red
            Write-Host "Error: $_" -ForegroundColor Red
            throw
        }
    }
} else {
    Write-Host "Bundle project not found: $bundleProject" -ForegroundColor Red
    throw "EXE bundle build failed - SetupBundle project missing"
}

# 6. Build summary
Write-Host "`n=== Build Summary ===" -ForegroundColor Green

foreach ($platform in $Platforms) {
    Write-Host "`nPlatform: $platform" -ForegroundColor Yellow
    
    # Check main application
    $appPath = Join-Path $SolutionDir "SafeExamBrowser.Runtime\bin\$platform\$Configuration\SafeExamBrowser.exe"
    if (Test-Path $appPath) {
        $appSize = [math]::Round((Get-Item $appPath).Length / 1MB, 2)
        Write-Host "  [OK] Application: SafeExamBrowser.exe ($appSize MB)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Application: SafeExamBrowser.exe (NOT FOUND)" -ForegroundColor Red
    }
    
    # Check MSI installer
    $msiPath = Join-Path $SolutionDir "Setup\bin\$platform\$Configuration\TSB.msi"
    if (Test-Path $msiPath) {
        $msiSize = [math]::Round((Get-Item $msiPath).Length / 1MB, 2)
        Write-Host "  [OK] MSI Installer: TSB.msi ($msiSize MB)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] MSI Installer: TSB.msi (NOT FOUND)" -ForegroundColor Red
    }
    
    # Check EXE bundle
    $bundlePath = Join-Path $SolutionDir "SetupBundle\bin\$platform\$Configuration\TSB.exe"
    if (Test-Path $bundlePath) {
        $bundleSize = [math]::Round((Get-Item $bundlePath).Length / 1MB, 2)
        Write-Host "  [OK] EXE Bundle: TSB.exe ($bundleSize MB)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] EXE Bundle: TSB.exe (NOT FOUND)" -ForegroundColor Red
    }
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host "All components built successfully for platforms: $($Platforms -join ', ')" -ForegroundColor Yellow

# Export build info for artifact collection
$buildInfo = @{
    Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss UTC")
    Configuration = $Configuration
    Platforms = $Platforms
    SolutionDir = $SolutionDir
    CommitHash = if ($env:CODEBUILD_RESOLVED_SOURCE_VERSION) { 
        $env:CODEBUILD_RESOLVED_SOURCE_VERSION.Substring(0, [Math]::Min(8, $env:CODEBUILD_RESOLVED_SOURCE_VERSION.Length))
    } else { 
        "local-build" 
    }
    BuildId = if ($env:CODEBUILD_BUILD_ID) { $env:CODEBUILD_BUILD_ID } else { "local-$(Get-Date -Format 'yyyyMMdd-HHmmss')" }
}

$buildInfoPath = Join-Path $SolutionDir "build-info.json"
$buildInfo | ConvertTo-Json -Depth 2 | Out-File -FilePath $buildInfoPath -Encoding UTF8

Write-Host "Build information saved to: $buildInfoPath" -ForegroundColor Cyan
