# TSB Complete Build and Deploy Pipeline
# Master script that orchestrates the entire build and deployment process

param(
    [string]$Configuration = "Release",
    [string[]]$Platforms = @("x64", "x86"),
    [string]$S3Bucket = "",
    [string]$S3Prefix = "",
    [string]$AwsRegion = "us-east-1",
    [switch]$SkipSetup = $false,
    [switch]$SkipBuild = $false,
    [switch]$SkipTests = $false,
    [switch]$SkipUpload = $false,
    [switch]$DryRun = $false,
    [switch]$Local = $false,
    [switch]$Sign = $false,
    [string]$KmsRegion = "",
    [string]$KmsKeyId = "",
    [string]$CertFile = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$SignDescription = "Topin Secure Browser",
    [string]$JsignPath = "jsign",
    [string]$AwsCredentials = "",
    [switch]$Help = $false
)

# Show help if requested
if ($Help) {
    Write-Host @"
TSB Complete Build and Deploy Pipeline

USAGE:
    .\build-and-deploy.ps1 [OPTIONS]

OPTIONS:
    -Configuration <string>     Build configuration (Default: Release)
    -Platforms <string[]>       Target platforms (Default: x64, x86)
    -S3Bucket <string>          S3 bucket name for deployment (Required for upload)
    -S3Prefix <string>          S3 key prefix (Optional)
    -AwsRegion <string>         AWS region (Default: us-east-1)
    -SkipSetup                  Skip environment setup
    -SkipBuild                  Skip build process
    -SkipTests                  Skip unit tests
    -SkipUpload                 Skip S3 upload
    -DryRun                     Perform dry run (no actual upload)
    -Local                      Force local development mode
    -Sign                       Authenticode sign the MSI/EXE outputs via jsign + AWS KMS
    -KmsRegion <string>         AWS region holding the KMS key (required with -Sign)
    -KmsKeyId <string>          KMS key id or alias, e.g. alias/nw-ev-code-signing (required with -Sign)
    -CertFile <string>          Path to the certificate chain (.pem/.p7b/.cer) (required with -Sign)
    -TimestampUrl <string>      RFC 3161 timestamp server URL (Default: http://timestamp.digicert.com)
    -SignDescription <string>   Description embedded in the signature (Default: Topin Secure Browser)
    -JsignPath <string>         'jsign' on PATH or path to jsign.jar (Default: jsign)
    -AwsCredentials <string>    Optional "accessKey|secretKey|sessionToken" (else uses AWS default chain)
    -Help                       Show this help message

EXAMPLES:
    # Complete build and deploy
    .\build-and-deploy.ps1 -S3Bucket "my-tsb-releases"

    # Build only (no upload)
    .\build-and-deploy.ps1 -SkipUpload

    # Quick build (skip setup and tests)
    .\build-and-deploy.ps1 -SkipSetup -SkipTests -SkipUpload

    # Dry run deployment
    .\build-and-deploy.ps1 -S3Bucket "my-tsb-releases" -DryRun

    # Build for x64 only
    .\build-and-deploy.ps1 -Platforms x64 -SkipUpload

ENVIRONMENT VARIABLES:
    CODEBUILD_SRC_DIR          Source directory (auto-detected in CodeBuild)
    CODEBUILD_BUILD_ID         Build ID (auto-detected in CodeBuild)
    MSBUILD_PATH              MSBuild executable path
    WIX                       WiX Toolset directory
"@ -ForegroundColor Cyan
    exit 0
}

Write-Host @"
=================================================================
    TSB (Topin Secure Browser) - Build and Deploy Pipeline
=================================================================
"@ -ForegroundColor Green

# Detect environment
$IsCodeBuild = $env:CODEBUILD_BUILD_ID -ne $null
$IsLocal = $Local -or (-not $IsCodeBuild)

Write-Host "Environment: $(if($IsCodeBuild) {'AWS CodeBuild'} else {'Local Development'})" -ForegroundColor Yellow
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow

if ($S3Bucket) {
    $s3Path = if ($S3Prefix) { "s3://$S3Bucket/$S3Prefix" } else { "s3://$S3Bucket" }
    Write-Host "S3 Deployment: $s3Path" -ForegroundColor Yellow
} else {
    Write-Host "S3 Deployment: Disabled" -ForegroundColor Yellow
}

# Set common variables
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Get script directory and solution directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionDir = Split-Path -Parent $ScriptDir

Write-Host "Script Directory: $ScriptDir" -ForegroundColor Cyan
Write-Host "Solution Directory: $SolutionDir" -ForegroundColor Cyan

# Validate required parameters
if (-not $SkipUpload -and -not $S3Bucket -and -not $DryRun) {
    Write-Host "ERROR: S3Bucket parameter is required for deployment. Use -SkipUpload to build only." -ForegroundColor Red
    exit 1
}

# Track timing
$StepTimes = @{}
$PipelineStartTime = Get-Date

# Function to execute pipeline step
function Invoke-PipelineStep($stepName, $scriptPath, $arguments = @(), $skipCondition = $false) {
    if ($skipCondition) {
        Write-Host "`n[$stepName] SKIPPED" -ForegroundColor Yellow
        return $true
    }
    
    Write-Host "`n=== STEP: $stepName ===" -ForegroundColor Magenta
    $stepStartTime = Get-Date
    
    try {
        $fullScriptPath = Join-Path $ScriptDir $scriptPath
        
        if (-not (Test-Path $fullScriptPath)) {
            throw "Script not found: $fullScriptPath"
        }
        
        Write-Host "Executing: $scriptPath $($arguments -join ' ')" -ForegroundColor Gray
        
        # Execute the script
        & $fullScriptPath @arguments
        
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
            throw "Script exited with code: $LASTEXITCODE"
        }
        
        $stepEndTime = Get-Date
        $stepDuration = $stepEndTime - $stepStartTime
        $StepTimes[$stepName] = $stepDuration
        
        Write-Host "[$stepName] COMPLETED in $($stepDuration.TotalMinutes.ToString('F1')) minutes" -ForegroundColor Green
        return $true
        
    } catch {
        $stepEndTime = Get-Date
        $stepDuration = $stepEndTime - $stepStartTime
        $StepTimes[$stepName] = $stepDuration
        
        Write-Host "[$stepName] FAILED after $($stepDuration.TotalMinutes.ToString('F1')) minutes" -ForegroundColor Red
        Write-Host "Error: $_" -ForegroundColor Red
        return $false
    }
}

# Pipeline execution
Write-Host "`nStarting TSB build pipeline..." -ForegroundColor Green
$pipelineId = if ($env:CODEBUILD_BUILD_ID) { $env:CODEBUILD_BUILD_ID } else { "local-$(Get-Date -Format 'yyyyMMdd-HHmmss')" }
Write-Host "Pipeline ID: $pipelineId" -ForegroundColor Cyan

$PipelineSuccess = $true

# Step 1: Environment Setup
if ($PipelineSuccess) {
    $setupArgs = @{}
    if ($Local) { $setupArgs.Local = $true }
    
    $PipelineSuccess = Invoke-PipelineStep "Environment Setup" "setup-environment.ps1" $setupArgs $SkipSetup
}

# Step 2: Build Application
if ($PipelineSuccess) {
    $buildArgs = @{
        Configuration = $Configuration
        Platforms = $Platforms
    }
    if ($SkipTests) { $buildArgs.SkipTests = $true }
    if ($Local) { $buildArgs.Local = $true }
    if ($Sign) {
        $buildArgs.Sign = $true
        $buildArgs.KmsRegion = $KmsRegion
        $buildArgs.KmsKeyId = $KmsKeyId
        $buildArgs.CertFile = $CertFile
        $buildArgs.TimestampUrl = $TimestampUrl
        $buildArgs.SignDescription = $SignDescription
        $buildArgs.JsignPath = $JsignPath
        if ($AwsCredentials) { $buildArgs.AwsCredentials = $AwsCredentials }
    }
    
    $PipelineSuccess = Invoke-PipelineStep "Build Application" "build-application.ps1" $buildArgs $SkipBuild
}

# Step 3: Collect Artifacts
if ($PipelineSuccess) {
    $collectArgs = @{
        Configuration = $Configuration
        Platforms = $Platforms
    }
    if ($Local) { $collectArgs.Local = $true }
    
    $PipelineSuccess = Invoke-PipelineStep "Collect Artifacts" "collect-artifacts.ps1" $collectArgs $false
}

# Step 4: Upload to S3
if ($PipelineSuccess -and $S3Bucket) {
    $uploadArgs = @{
        BucketName = $S3Bucket
        Region = $AwsRegion
    }
    if ($S3Prefix) { $uploadArgs.BucketPrefix = $S3Prefix }
    if ($DryRun) { $uploadArgs.DryRun = $true }
    if ($Local) { $uploadArgs.Local = $true }
    
    $PipelineSuccess = Invoke-PipelineStep "Upload to S3" "upload-to-s3.ps1" $uploadArgs $SkipUpload
}

# Pipeline completion
$PipelineEndTime = Get-Date
$TotalDuration = $PipelineEndTime - $PipelineStartTime

Write-Host "`n=================================================================" -ForegroundColor Green

if ($PipelineSuccess) {
    Write-Host "    ✓ TSB BUILD PIPELINE COMPLETED SUCCESSFULLY" -ForegroundColor Green
} else {
    Write-Host "    ✗ TSB BUILD PIPELINE FAILED" -ForegroundColor Red
}

Write-Host "=================================================================" -ForegroundColor Green

# Timing summary
Write-Host "`nTiming Summary:" -ForegroundColor Yellow
foreach ($step in $StepTimes.GetEnumerator()) {
    $minutes = $step.Value.TotalMinutes.ToString('F1')
    Write-Host "  $($step.Key): $minutes minutes" -ForegroundColor Cyan
}
Write-Host "  Total Pipeline: $($TotalDuration.TotalMinutes.ToString('F1')) minutes" -ForegroundColor Yellow

# Artifact summary
$artifactInfoPath = Join-Path $SolutionDir "artifact-info.json"
if (Test-Path $artifactInfoPath) {
    $artifactInfo = Get-Content $artifactInfoPath -Raw | ConvertFrom-Json
    
    Write-Host "`nArtifact Summary:" -ForegroundColor Yellow
    Write-Host "  Total Size: $([math]::Round($artifactInfo.TotalSize / 1MB, 2)) MB" -ForegroundColor Cyan
    Write-Host "  File Count: $($artifactInfo.FileCount)" -ForegroundColor Cyan
    Write-Host "  Platforms: $($artifactInfo.Platforms -join ', ')" -ForegroundColor Cyan
}

# Deployment URLs (if uploaded)
if ($PipelineSuccess -and $S3Bucket -and -not $SkipUpload -and -not $DryRun) {
    Write-Host "`nDeployment URLs:" -ForegroundColor Yellow
    
    $baseUrl = "https://$S3Bucket.s3.$AwsRegion.amazonaws.com"
    if ($S3Prefix) { $baseUrl += "/$S3Prefix" }
    
    Write-Host "  EXE Bundle: $baseUrl/TSB.exe" -ForegroundColor Cyan
    Write-Host "  MSI Installer: $baseUrl/TSB.msi" -ForegroundColor Cyan
    Write-Host "  Standalone App: $baseUrl/SafeExamBrowser.exe" -ForegroundColor Cyan
    Write-Host "  Deployment Manifest: $baseUrl/deployment-manifest.json" -ForegroundColor Cyan
}

# Next steps
Write-Host "`nNext Steps:" -ForegroundColor Yellow

if ($PipelineSuccess) {
    if ($SkipUpload) {
        Write-Host "  • Artifacts are ready in the 'artifacts/deploy' directory" -ForegroundColor Cyan
        Write-Host "  • Run with -S3Bucket parameter to deploy to S3" -ForegroundColor Cyan
    } elseif ($S3Bucket -and -not $DryRun) {
        Write-Host "  • Build artifacts are now available in S3" -ForegroundColor Cyan
        Write-Host "  • Update your download links to point to the new files" -ForegroundColor Cyan
        Write-Host "  • Test the deployed installers" -ForegroundColor Cyan
    } elseif ($DryRun) {
        Write-Host "  • This was a dry run - run without -DryRun to actually deploy" -ForegroundColor Cyan
    }
} else {
    Write-Host "  • Check the error messages above" -ForegroundColor Red
    Write-Host "  • Fix any issues and re-run the pipeline" -ForegroundColor Red
    Write-Host "  • Use individual scripts for debugging specific steps" -ForegroundColor Red
}

# Environment-specific notes
if ($IsCodeBuild) {
    Write-Host "`nCodeBuild Notes:" -ForegroundColor Yellow
    Write-Host "  • Build logs are available in CloudWatch" -ForegroundColor Cyan
    Write-Host "  • Artifacts are automatically cleaned up after the build" -ForegroundColor Cyan
} else {
    Write-Host "`nLocal Development Notes:" -ForegroundColor Yellow
    Write-Host "  • Artifacts remain in the 'artifacts' directory" -ForegroundColor Cyan
    Write-Host "  • Clean up manually if needed: Remove-Item artifacts -Recurse -Force" -ForegroundColor Cyan
}

Write-Host "`n=================================================================" -ForegroundColor Green

# Exit with appropriate code
exit $(if ($PipelineSuccess) { 0 } else { 1 })
