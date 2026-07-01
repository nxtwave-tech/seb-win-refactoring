# Upload the signed TSB installers to S3
# Run this right after a signed build (see SIGNING.md). It verifies the Authenticode
# signature of each platform bundle, then uploads them to a clean, versioned S3 layout.
#
# Layout (bucket "topin-secure-browser", channel "beta"):
#   beta/windows/tsb.exe                  <- rolling "latest" pointer (x64, primary)
#   beta/windows/tsb_x86.exe              <- rolling "latest" pointer (x86)
#   beta/windows/tsb_latest.json          <- version / sha256 / commit / time (all platforms)
#   beta/windows/<version>/tsb.exe        <- immutable versioned archive (x64)
#   beta/windows/<version>/tsb_x86.exe    <- immutable versioned archive (x86)

param(
    [Parameter(Mandatory = $true)]
    [string]$AccessKey,

    [Parameter(Mandatory = $true)]
    [string]$SecretKey,

    [string]$SessionToken = "",

    [string]$Bucket = "topin-secure-browser",
    [string]$Channel = "beta",
    [string]$Region = "ap-south-1",

    [ValidateSet("x64", "x86")]
    [string[]]$Platforms = @("x64", "x86"),

    [string]$Configuration = "Release",

    [switch]$SkipSignatureCheck = $false,
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

Write-Host "=== TSB Signed Upload to S3 ===" -ForegroundColor Green

$SolutionDir = if ($env:CODEBUILD_SRC_DIR) { $env:CODEBUILD_SRC_DIR } else { (Get-Location).Path }

Write-Host "Bucket:    s3://$Bucket/$Channel/windows" -ForegroundColor Cyan
Write-Host "Region:    $Region" -ForegroundColor Cyan
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Cyan
Write-Host "Dry run:   $(if ($DryRun) { 'Yes' } else { 'No' })" -ForegroundColor Cyan

# The primary platform (x64) keeps the clean "tsb.exe" name; others get a platform suffix.
function Get-InstallerName($platform) {
    if ($platform -eq "x64") { "tsb.exe" } else { "tsb_$platform.exe" }
}

# Commit is shared across platforms (build-info.json, else git).
$commit = $null
$buildInfoPath = Join-Path $SolutionDir "build-info.json"
if (Test-Path $buildInfoPath) {
    try { $commit = (Get-Content $buildInfoPath -Raw | ConvertFrom-Json).CommitHash } catch { }
}
if (-not $commit) {
    try { $commit = (git -C $SolutionDir rev-parse --short HEAD 2>$null) } catch { }
}
if (-not $commit) { $commit = "unknown" }

# 1. Resolve + verify each platform bundle before uploading anything.
$builds = @()
foreach ($platform in $Platforms) {
    $filePath = Join-Path $SolutionDir "SetupBundle\bin\$platform\$Configuration\TSB.exe"
    if (-not (Test-Path $filePath)) {
        throw "Installer not found for $platform`: $filePath. Run a signed build first (see SIGNING.md)."
    }
    $filePath = (Resolve-Path $filePath).Path
    $fileItem = Get-Item $filePath
    $fileSizeMb = [math]::Round($fileItem.Length / 1MB, 2)

    Write-Host "`n[$platform] $filePath ($fileSizeMb MB)" -ForegroundColor Cyan

    if (-not $SkipSignatureCheck) {
        $sig = Get-AuthenticodeSignature -FilePath $filePath
        if ($sig.Status -ne "Valid") {
            throw "[$platform] Signature check failed: Status=$($sig.Status). $($sig.StatusMessage) " +
                  "Sign the build first, or pass -SkipSignatureCheck to override."
        }
        Write-Host "  [OK] Signature valid - signer: $($sig.SignerCertificate.Subject)" -ForegroundColor Green
    } else {
        Write-Host "  [WARN] Skipping signature verification (-SkipSignatureCheck)." -ForegroundColor Yellow
    }

    $version = $fileItem.VersionInfo.ProductVersion
    if (-not $version) { $version = $fileItem.VersionInfo.FileVersion }
    if ($version) { $version = $version.Trim() }
    if (-not $version) { $version = (Get-Date -Format "yyyyMMdd-HHmmss") }

    $sha256 = (Get-FileHash -Path $filePath -Algorithm SHA256).Hash.ToLower()

    Write-Host "  Version: $version | SHA-256: $sha256" -ForegroundColor Cyan

    $builds += [pscustomobject]@{
        Platform   = $platform
        FilePath   = $filePath
        FileName   = Get-InstallerName $platform
        Version    = $version
        Sha256     = $sha256
        SizeBytes  = $fileItem.Length
    }
}

# 2. Ensure AWS CLI is available.
try {
    $awsVersion = (aws --version) 2>&1
    Write-Host "`nAWS CLI:   $awsVersion" -ForegroundColor Cyan
} catch {
    throw "AWS CLI not found on PATH. Install it from https://aws.amazon.com/cli/."
}

# 3. Scope the provided credentials to this process only (never written to disk/config).
$env:AWS_ACCESS_KEY_ID = $AccessKey
$env:AWS_SECRET_ACCESS_KEY = $SecretKey
if ($SessionToken) { $env:AWS_SESSION_TOKEN = $SessionToken } else { $env:AWS_SESSION_TOKEN = $null }
$env:AWS_DEFAULT_REGION = $Region

$basePrefix = "$Channel/windows"

function Invoke-S3Copy($localPath, $key, $contentType, $cacheControl) {
    $s3Uri = "s3://$Bucket/$key"
    if ($DryRun) {
        Write-Host "  [DRY RUN] $localPath -> $s3Uri" -ForegroundColor Yellow
        return
    }
    $awsArgs = @(
        "s3", "cp", $localPath, $s3Uri,
        "--region", $Region,
        "--content-type", $contentType,
        "--cache-control", $cacheControl,
        "--only-show-errors"
    )
    Write-Host "  Uploading -> $s3Uri" -ForegroundColor Cyan
    & aws @awsArgs
    if ($LASTEXITCODE -ne 0) { throw "Upload failed for $s3Uri (aws exit code $LASTEXITCODE)." }
    Write-Host "  [OK] $s3Uri" -ForegroundColor Green
}

# 4. Upload each platform: immutable versioned copy, then the rolling "latest" pointer.
Write-Host "`nUploading installers..." -ForegroundColor Yellow
foreach ($b in $builds) {
    $versionedKey = "$basePrefix/$($b.Version)/$($b.FileName)"
    $latestKey = "$basePrefix/$($b.FileName)"
    Invoke-S3Copy $b.FilePath $versionedKey "application/octet-stream" "public, max-age=31536000, immutable"
    Invoke-S3Copy $b.FilePath $latestKey "application/octet-stream" "no-cache"
}

# 5. Build and upload the combined latest-version metadata document.
Write-Host "`nUploading metadata..." -ForegroundColor Yellow
$metadata = [ordered]@{
    product     = "Topin Secure Browser"
    channel     = $Channel
    commit      = $commit
    uploaded_at = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    platforms   = @(
        $builds | ForEach-Object {
            [ordered]@{
                platform   = $_.Platform
                version    = $_.Version
                file       = $_.FileName
                versioned  = "$($_.Version)/$($_.FileName)"
                sha256     = $_.Sha256
                size_bytes = $_.SizeBytes
            }
        }
    )
}
$metaPath = Join-Path $env:TEMP "tsb_latest.json"
$metadata | ConvertTo-Json -Depth 4 | Out-File -FilePath $metaPath -Encoding UTF8

$latestMetaKey = "$basePrefix/tsb_latest.json"
if ($DryRun) {
    Write-Host "  [DRY RUN] metadata -> s3://$Bucket/$latestMetaKey" -ForegroundColor Yellow
} else {
    & aws s3 cp $metaPath "s3://$Bucket/$latestMetaKey" --region $Region `
        --content-type "application/json" --cache-control "no-cache" --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "Upload failed for metadata (aws exit code $LASTEXITCODE)." }
    Write-Host "  [OK] s3://$Bucket/$latestMetaKey" -ForegroundColor Green
}
Remove-Item $metaPath -ErrorAction SilentlyContinue

# 6. Summary
Write-Host "`n=== Upload Complete ===" -ForegroundColor Green
$baseUrl = "https://$Bucket.s3.$Region.amazonaws.com"
foreach ($b in $builds) {
    Write-Host "$($b.Platform) latest:    $baseUrl/$basePrefix/$($b.FileName)" -ForegroundColor Cyan
    Write-Host "$($b.Platform) versioned: $baseUrl/$basePrefix/$($b.Version)/$($b.FileName)" -ForegroundColor Cyan
}
Write-Host "metadata:      $baseUrl/$latestMetaKey" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "`nThis was a dry run - nothing was uploaded." -ForegroundColor Yellow
}
