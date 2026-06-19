# TSB CI/CD Build Scripts

This directory contains PowerShell scripts for building and deploying the Topin Secure Browser (TSB) application in both local development and AWS CodeBuild environments.

## Quick Start

### Complete Build and Deploy
```powershell
# Build and deploy to S3
.\scripts\build-and-deploy.ps1 -S3Bucket "your-bucket-name"

# Build only (no deployment)
.\scripts\build-and-deploy.ps1 -SkipUpload
```

### Individual Scripts
```powershell
# 1. Setup environment
.\scripts\setup-environment.ps1

# 2. Build application
.\scripts\build-application.ps1

# 3. Collect artifacts
.\scripts\collect-artifacts.ps1

# 4. Upload to S3
.\scripts\upload-to-s3.ps1 -BucketName "your-bucket-name"
```

## Scripts Overview

### 1. `setup-environment.ps1`
**Purpose**: Installs and configures the build environment
- Visual Studio Build Tools 2022
- WiX Toolset v3.14
- .NET Framework 4.8 Developer Pack
- NuGet CLI

**Usage**:
```powershell
.\setup-environment.ps1 [-Local]
```

### 2. `build-application.ps1`
**Purpose**: Builds the complete TSB application
- Restores NuGet packages
- Builds main application (x64/x86)
- Runs unit tests (optional)
- Builds MSI installers
- Builds EXE bundles

**Usage**:
```powershell
.\build-application.ps1 [-Configuration Release] [-Platforms x64,x86] [-SkipTests] [-Local]
```

### 3. `collect-artifacts.ps1`
**Purpose**: Organizes build outputs into deployment-ready structure
- Collects all distributable files
- Creates platform-specific directories
- Prepares clean deployment structure
- Generates artifact metadata

**Usage**:
```powershell
.\collect-artifacts.ps1 [-Configuration Release] [-Platforms x64,x86] [-OutputDir artifacts] [-Local]
```

### 4. `upload-to-s3.ps1`
**Purpose**: Uploads artifacts to S3 bucket
- Uploads main distributable files
- Uploads platform-specific files
- Uploads documentation
- Creates deployment manifest
- Sets proper metadata and content types

**Usage**:
```powershell
.\upload-to-s3.ps1 -BucketName "bucket" [-BucketPrefix "prefix"] [-Region us-east-1] [-DryRun] [-Local]
```

### 5. `build-and-deploy.ps1` (Master Script)
**Purpose**: Orchestrates the complete pipeline
- Runs all scripts in sequence
- Provides comprehensive error handling
- Tracks timing and generates reports
- Supports partial execution with skip flags

**Usage**:
```powershell
.\build-and-deploy.ps1 [OPTIONS]

OPTIONS:
    -Configuration <string>     Build configuration (Default: Release)
    -Platforms <string[]>       Target platforms (Default: x64, x86)
    -S3Bucket <string>          S3 bucket name for deployment
    -S3Prefix <string>          S3 key prefix (Optional)
    -AwsRegion <string>         AWS region (Default: us-east-1)
    -SkipSetup                  Skip environment setup
    -SkipBuild                  Skip build process
    -SkipTests                  Skip unit tests
    -SkipUpload                 Skip S3 upload
    -DryRun                     Perform dry run (no actual upload)
    -Local                      Force local development mode
    -Help                       Show help message
```

## Environment Detection

The scripts automatically detect whether they're running in:
- **AWS CodeBuild**: Uses `CODEBUILD_BUILD_ID` environment variable
- **Local Development**: Falls back to local mode

## Output Structure

### Artifacts Directory
```
artifacts/
├── x64/                          # x64 platform files
│   ├── Application/              # Complete application
│   ├── TSB.msi                   # MSI installer
│   └── TSB.exe                   # EXE bundle
├── x86/                          # x86 platform files
│   ├── Application/              # Complete application
│   ├── TSB.msi                   # MSI installer
│   └── TSB.exe                   # EXE bundle
└── deploy/                       # Deployment-ready files
    ├── TSB.exe                   # Primary EXE bundle (x64)
    ├── TSB.msi                   # Primary MSI installer (x64)
    ├── TSB-x86.exe               # x86 EXE bundle
    ├── TSB-x86.msi               # x86 MSI installer
    ├── SafeExamBrowser.exe       # Primary standalone app (x64)
    ├── SafeExamBrowser-x86.exe   # x86 standalone app
    ├── Application/              # Complete application directory
    ├── build-info.json           # Build metadata
    └── LICENSE.txt               # Documentation files
```

### S3 Structure
```
s3://your-bucket/
├── TSB.exe                       # Main EXE bundle (clean URL)
├── TSB.msi                       # Main MSI installer (clean URL)
├── SafeExamBrowser.exe           # Main standalone app (clean URL)
├── TSB-x86.exe                   # x86 EXE bundle
├── TSB-x86.msi                   # x86 MSI installer
├── SafeExamBrowser-x86.exe       # x86 standalone app
├── Application/                  # Complete application files
├── build-info.json               # Build information
├── deployment-manifest.json      # Deployment metadata
└── LICENSE.txt                   # Documentation
```

## AWS CodeBuild Integration

### buildspec.yml Example
```yaml
version: 0.2

phases:
  install:
    runtime-versions:
      dotnet: 4.8
  
  pre_build:
    commands:
      - echo "Starting TSB build pipeline"
  
  build:
    commands:
      - powershell -ExecutionPolicy Bypass -File scripts/build-and-deploy.ps1 -S3Bucket $S3_BUCKET_NAME -S3Prefix $S3_PREFIX
  
  post_build:
    commands:
      - echo "Build completed"

artifacts:
  files:
    - 'artifacts/**/*'
  name: tsb-build-artifacts
```

### Environment Variables
Set these in your CodeBuild project:
- `S3_BUCKET_NAME`: Your S3 bucket name
- `S3_PREFIX`: Optional S3 key prefix
- `AWS_DEFAULT_REGION`: AWS region for S3

## Local Development

### Prerequisites
- Windows 10/11 x64
- PowerShell 5.1 or later
- Internet connection (for downloading tools)

### First Run
```powershell
# Clone repository
git clone <your-repo-url>
cd seb-win-refactoring

# Run complete pipeline (will install tools automatically)
.\scripts\build-and-deploy.ps1 -SkipUpload

# Or run setup first, then build
.\scripts\setup-environment.ps1
.\scripts\build-application.ps1
```

### Development Workflow
```powershell
# Quick build (skip setup and tests)
.\scripts\build-and-deploy.ps1 -SkipSetup -SkipTests -SkipUpload

# Build specific platform
.\scripts\build-application.ps1 -Platforms x64

# Test deployment without uploading
.\scripts\build-and-deploy.ps1 -S3Bucket "test-bucket" -DryRun
```

## Troubleshooting

### Common Issues

1. **MSBuild not found**
   ```powershell
   # Run setup script to install Visual Studio Build Tools
   .\scripts\setup-environment.ps1
   ```

2. **WiX Toolset not found**
   ```powershell
   # Setup script will install WiX automatically
   .\scripts\setup-environment.ps1
   ```

3. **NuGet restore fails**
   ```powershell
   # Clear NuGet cache
   nuget locals all -clear
   
   # Run build again
   .\scripts\build-application.ps1
   ```

4. **S3 upload fails**
   ```powershell
   # Check AWS credentials
   aws sts get-caller-identity
   
   # Test with dry run
   .\scripts\upload-to-s3.ps1 -BucketName "your-bucket" -DryRun
   ```

### Debug Mode
```powershell
# Enable verbose output
$VerbosePreference = "Continue"
.\scripts\build-and-deploy.ps1 -S3Bucket "your-bucket"
```

### Manual Cleanup
```powershell
# Clean artifacts
Remove-Item artifacts -Recurse -Force -ErrorAction SilentlyContinue

# Clean build outputs
Remove-Item */bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item */obj -Recurse -Force -ErrorAction SilentlyContinue
```

## Customization

### Adding New Platforms
Edit the default platforms in scripts:
```powershell
[string[]]$Platforms = @("x64", "x86", "ARM64")
```

### Custom S3 Structure
Modify the upload script to change S3 key patterns:
```powershell
$s3Key = "releases/v$version/$($file.Name)"
```

### Additional Artifacts
Add custom files to the collect-artifacts script:
```powershell
$customFiles = @("custom-config.xml", "deployment-guide.pdf")
foreach ($file in $customFiles) {
    Copy-ArtifactFile $file (Join-Path $deployDir $file) "Custom File"
}
```

## Support

For issues with these build scripts:
1. Check the error messages in the console output
2. Review the generated log files in the artifacts directory
3. Run individual scripts to isolate the problem
4. Use `-DryRun` flag to test without making changes



