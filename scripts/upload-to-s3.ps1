# Upload TSB Artifacts to S3
# Uploads build artifacts to S3 bucket with proper structure and metadata

param(
    [Parameter(Mandatory=$true)]
    [string]$BucketName,
    
    [string]$BucketPrefix = "",
    [string]$ArtifactsDir = "artifacts",
    [string]$Region = "us-east-1",
    [switch]$DryRun = $false,
    [switch]$Local = $false
)

Write-Host "=== TSB S3 Upload ===" -ForegroundColor Green

# Detect environment
$IsCodeBuild = $env:CODEBUILD_BUILD_ID -ne $null
$IsLocal = $Local -or (-not $IsCodeBuild)

Write-Host "Environment: $(if($IsCodeBuild) {'AWS CodeBuild'} else {'Local Development'})" -ForegroundColor Yellow
Write-Host "S3 Bucket: s3://$BucketName$(if($BucketPrefix) {"/$BucketPrefix"} else {""})" -ForegroundColor Yellow
Write-Host "AWS Region: $Region" -ForegroundColor Yellow
Write-Host "Dry Run: $(if($DryRun) {'Yes'} else {'No'})" -ForegroundColor Yellow

# Set common variables
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Get paths
$SolutionDir = if ($env:CODEBUILD_SRC_DIR) { $env:CODEBUILD_SRC_DIR } else { (Get-Location).Path }
$ArtifactsPath = Join-Path $SolutionDir $ArtifactsDir
$DeployPath = Join-Path $ArtifactsPath "deploy"

Write-Host "Solution Directory: $SolutionDir" -ForegroundColor Cyan
Write-Host "Artifacts Directory: $ArtifactsPath" -ForegroundColor Cyan
Write-Host "Deploy Directory: $DeployPath" -ForegroundColor Cyan

# Validate directories
if (-not (Test-Path $ArtifactsPath)) {
    throw "Artifacts directory not found: $ArtifactsPath. Run collect-artifacts.ps1 first."
}

if (-not (Test-Path $DeployPath)) {
    throw "Deploy directory not found: $DeployPath. Run collect-artifacts.ps1 first."
}

# Check AWS CLI availability
try {
    $awsVersion = aws --version 2>&1
    Write-Host "AWS CLI: $awsVersion" -ForegroundColor Cyan
} catch {
    throw "AWS CLI not found. Please install AWS CLI and configure credentials."
}

# Verify AWS credentials
try {
    $awsIdentity = aws sts get-caller-identity --output json 2>&1 | ConvertFrom-Json
    Write-Host "AWS Identity: $($awsIdentity.Arn)" -ForegroundColor Cyan
} catch {
    throw "AWS credentials not configured or invalid. Please configure AWS credentials."
}

# Function to upload file to S3 with metadata
function Upload-FileToS3($localPath, $s3Key, $contentType = $null, $metadata = @{}) {
    if (-not (Test-Path $localPath)) {
        Write-Host "  ✗ File not found: $localPath" -ForegroundColor Red
        return $false
    }
    
    $fileSize = [math]::Round((Get-Item $localPath).Length / 1MB, 2)
    $s3Uri = "s3://$BucketName/$s3Key"
    
    if ($DryRun) {
        Write-Host "  [DRY RUN] Would upload: $localPath -> $s3Uri ($fileSize MB)" -ForegroundColor Yellow
        return $true
    }
    
    # Build AWS CLI command
    $awsArgs = @(
        "s3", "cp",
        "`"$localPath`"",
        "`"$s3Uri`"",
        "--region", $Region
    )
    
    # Add content type if specified
    if ($contentType) {
        $awsArgs += @("--content-type", $contentType)
    }
    
    # Add metadata
    if ($metadata.Count -gt 0) {
        $metadataString = ($metadata.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ","
        $awsArgs += @("--metadata", $metadataString)
    }
    
    try {
        Write-Host "  Uploading: $([System.IO.Path]::GetFileName($localPath)) ($fileSize MB)" -ForegroundColor Cyan
        
        $process = Start-Process -FilePath "aws" -ArgumentList $awsArgs -Wait -PassThru -NoNewWindow -RedirectStandardError $env:TEMP\aws-error.log
        
        if ($process.ExitCode -eq 0) {
            Write-Host "  ✓ Uploaded successfully" -ForegroundColor Green
            return $true
        } else {
            $errorContent = if (Test-Path "$env:TEMP\aws-error.log") { Get-Content "$env:TEMP\aws-error.log" -Raw } else { "Unknown error" }
            Write-Host "  ✗ Upload failed: $errorContent" -ForegroundColor Red
            return $false
        }
    } catch {
        Write-Host "  ✗ Upload failed: $_" -ForegroundColor Red
        return $false
    }
}

# Function to get content type based on file extension
function Get-ContentType($filePath) {
    $extension = [System.IO.Path]::GetExtension($filePath).ToLower()
    
    switch ($extension) {
        ".exe" { return "application/octet-stream" }
        ".msi" { return "application/octet-stream" }
        ".dll" { return "application/octet-stream" }
        ".json" { return "application/json" }
        ".txt" { return "text/plain" }
        ".md" { return "text/markdown" }
        ".config" { return "application/xml" }
        ".pdb" { return "application/octet-stream" }
        default { return "application/octet-stream" }
    }
}

# Load build and artifact information
$buildInfoPath = Join-Path $SolutionDir "build-info.json"
$artifactInfoPath = Join-Path $SolutionDir "artifact-info.json"

$buildInfo = if (Test-Path $buildInfoPath) { 
    Get-Content $buildInfoPath -Raw | ConvertFrom-Json 
} else { 
    @{ BuildId = "unknown"; Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss UTC") }
}

$artifactInfo = if (Test-Path $artifactInfoPath) { 
    Get-Content $artifactInfoPath -Raw | ConvertFrom-Json 
} else { 
    @{ TotalSize = 0; FileCount = 0 }
}

Write-Host "`nBuild Information:" -ForegroundColor Yellow
Write-Host "  Build ID: $($buildInfo.BuildId)" -ForegroundColor Cyan
Write-Host "  Timestamp: $($buildInfo.Timestamp)" -ForegroundColor Cyan
Write-Host "  Commit: $($buildInfo.CommitHash)" -ForegroundColor Cyan
Write-Host "  Total Size: $([math]::Round($artifactInfo.TotalSize / 1MB, 2)) MB" -ForegroundColor Cyan
Write-Host "  File Count: $($artifactInfo.FileCount)" -ForegroundColor Cyan

# Prepare metadata for uploads
$commonMetadata = @{
    "build-id" = $buildInfo.BuildId
    "build-timestamp" = $buildInfo.Timestamp
    "commit-hash" = $buildInfo.CommitHash
    "configuration" = $buildInfo.Configuration
}

# 1. Upload main distributable files (clean names for end users)
Write-Host "`n1. Uploading main distributable files..." -ForegroundColor Yellow

$mainFiles = @(
    @{ 
        LocalPath = Join-Path $DeployPath "TSB.exe"
        S3Key = if ($BucketPrefix) { "$BucketPrefix/TSB.exe" } else { "TSB.exe" }
        Description = "Main EXE Bundle Installer"
    },
    @{ 
        LocalPath = Join-Path $DeployPath "TSB.msi"
        S3Key = if ($BucketPrefix) { "$BucketPrefix/TSB.msi" } else { "TSB.msi" }
        Description = "Main MSI Installer"
    },
    @{ 
        LocalPath = Join-Path $DeployPath "Application\SafeExamBrowser.exe"
        S3Key = if ($BucketPrefix) { "$BucketPrefix/SafeExamBrowser.exe" } else { "SafeExamBrowser.exe" }
        Description = "Standalone Application"
    }
)

$uploadResults = @{}

foreach ($file in $mainFiles) {
    if (Test-Path $file.LocalPath) {
        $contentType = Get-ContentType $file.LocalPath
        $metadata = $commonMetadata.Clone()
        $metadata["file-type"] = "main-distributable"
        $metadata["description"] = $file.Description
        
        $success = Upload-FileToS3 $file.LocalPath $file.S3Key $contentType $metadata
        $uploadResults[$file.S3Key] = $success
    } else {
        Write-Host "  ✗ $($file.Description): File not found" -ForegroundColor Red
        $uploadResults[$file.S3Key] = $false
    }
}

# 2. Upload platform-specific files (with platform suffixes)
Write-Host "`n2. Uploading platform-specific files..." -ForegroundColor Yellow

$platformFiles = Get-ChildItem $DeployPath -File | Where-Object { $_.Name -match "-(x64|x86)\." }

foreach ($file in $platformFiles) {
    $s3Key = if ($BucketPrefix) { "$BucketPrefix/$($file.Name)" } else { $file.Name }
    $contentType = Get-ContentType $file.FullName
    $metadata = $commonMetadata.Clone()
    $metadata["file-type"] = "platform-specific"
    
    # Extract platform from filename
    if ($file.Name -match "-(x64|x86)\.") {
        $metadata["platform"] = $matches[1]
    }
    
    $success = Upload-FileToS3 $file.FullName $s3Key $contentType $metadata
    $uploadResults[$s3Key] = $success
}

# 3. Upload documentation and metadata files
Write-Host "`n3. Uploading documentation and metadata..." -ForegroundColor Yellow

$docFiles = @(
    @{ Name = "build-info.json"; Description = "Build Information" },
    @{ Name = "LICENSE.txt"; Description = "License" },
    @{ Name = "README.md"; Description = "README" },
    @{ Name = "BUILD_GUIDE.md"; Description = "Build Guide" }
)

foreach ($docFile in $docFiles) {
    $localPath = Join-Path $DeployPath $docFile.Name
    if (Test-Path $localPath) {
        $s3Key = if ($BucketPrefix) { "$BucketPrefix/$($docFile.Name)" } else { $docFile.Name }
        $contentType = Get-ContentType $localPath
        $metadata = $commonMetadata.Clone()
        $metadata["file-type"] = "documentation"
        $metadata["description"] = $docFile.Description
        
        $success = Upload-FileToS3 $localPath $s3Key $contentType $metadata
        $uploadResults[$s3Key] = $success
    }
}

# 4. Upload complete application directory (for advanced users)
Write-Host "`n4. Uploading complete application directory..." -ForegroundColor Yellow

$appDir = Join-Path $DeployPath "Application"
if (Test-Path $appDir) {
    $appFiles = Get-ChildItem $appDir -Recurse -File
    
    foreach ($appFile in $appFiles) {
        $relativePath = $appFile.FullName.Substring($appDir.Length + 1)
        $s3Key = if ($BucketPrefix) { 
            "$BucketPrefix/Application/$relativePath" 
        } else { 
            "Application/$relativePath" 
        }
        
        $contentType = Get-ContentType $appFile.FullName
        $metadata = $commonMetadata.Clone()
        $metadata["file-type"] = "application-component"
        
        $success = Upload-FileToS3 $appFile.FullName $s3Key $contentType $metadata
        $uploadResults[$s3Key] = $success
    }
}

# 5. Create and upload deployment manifest
Write-Host "`n5. Creating deployment manifest..." -ForegroundColor Yellow

$manifest = @{
    deployment = @{
        timestamp = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
        buildInfo = $buildInfo
        artifactInfo = $artifactInfo
    }
    files = @{
        main = @{
            installer_exe = if ($BucketPrefix) { "$BucketPrefix/TSB.exe" } else { "TSB.exe" }
            installer_msi = if ($BucketPrefix) { "$BucketPrefix/TSB.msi" } else { "TSB.msi" }
            application = if ($BucketPrefix) { "$BucketPrefix/SafeExamBrowser.exe" } else { "SafeExamBrowser.exe" }
        }
        platform_specific = @()
        documentation = @()
    }
    upload_results = $uploadResults
}

# Add platform-specific files to manifest
foreach ($file in $platformFiles) {
    $s3Key = if ($BucketPrefix) { "$BucketPrefix/$($file.Name)" } else { $file.Name }
    $manifest.files.platform_specific += @{
        name = $file.Name
        s3_key = $s3Key
        size_mb = [math]::Round($file.Length / 1MB, 2)
    }
}

# Add documentation files to manifest
foreach ($docFile in $docFiles) {
    $localPath = Join-Path $DeployPath $docFile.Name
    if (Test-Path $localPath) {
        $s3Key = if ($BucketPrefix) { "$BucketPrefix/$($docFile.Name)" } else { $docFile.Name }
        $manifest.files.documentation += @{
            name = $docFile.Name
            s3_key = $s3Key
            description = $docFile.Description
        }
    }
}

# Save and upload manifest
$manifestPath = Join-Path $SolutionDir "deployment-manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Out-File -FilePath $manifestPath -Encoding UTF8

$manifestS3Key = if ($BucketPrefix) { "$BucketPrefix/deployment-manifest.json" } else { "deployment-manifest.json" }
$manifestMetadata = $commonMetadata.Clone()
$manifestMetadata["file-type"] = "deployment-manifest"

Upload-FileToS3 $manifestPath $manifestS3Key "application/json" $manifestMetadata | Out-Null

# 6. Upload summary
Write-Host "`n=== Upload Summary ===" -ForegroundColor Green

$successCount = ($uploadResults.Values | Where-Object { $_ -eq $true }).Count
$totalCount = $uploadResults.Count

Write-Host "Files uploaded: $successCount / $totalCount" -ForegroundColor Yellow

if ($successCount -eq $totalCount) {
    Write-Host "✓ All files uploaded successfully!" -ForegroundColor Green
} else {
    Write-Host "✗ Some files failed to upload" -ForegroundColor Red
    
    Write-Host "`nFailed uploads:" -ForegroundColor Red
    foreach ($result in $uploadResults.GetEnumerator()) {
        if (-not $result.Value) {
            Write-Host "  - $($result.Key)" -ForegroundColor Red
        }
    }
}

# Generate public URLs (if bucket is public)
Write-Host "`nPublic URLs (if bucket is configured for public access):" -ForegroundColor Yellow
foreach ($file in $mainFiles) {
    if ($uploadResults[$file.S3Key]) {
        $publicUrl = "https://$BucketName.s3.$Region.amazonaws.com/$($file.S3Key)"
        Write-Host "  $($file.Description): $publicUrl" -ForegroundColor Cyan
    }
}

Write-Host "`n=== S3 Upload Complete ===" -ForegroundColor Green

if (-not $DryRun) {
    Write-Host "Deployment manifest: s3://$BucketName/$manifestS3Key" -ForegroundColor Cyan
    Write-Host "Build artifacts are now available in S3!" -ForegroundColor Yellow
} else {
    Write-Host "This was a dry run - no files were actually uploaded." -ForegroundColor Yellow
}

# Return success status
exit $(if ($successCount -eq $totalCount) { 0 } else { 1 })



