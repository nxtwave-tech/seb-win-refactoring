# Topin Secure Browser - Code Signing Guide

The MSI installers and EXE bundle are Authenticode-signed with a GlobalSign EV code signing
certificate whose private key is held in **AWS KMS** (KMS acts as the FIPS 140-2 L3 HSM).
Because AWS KMS has no native Windows signing provider, signing is done with
[jsign](https://ebourg.github.io/jsign/), which calls `kms:Sign` directly.

## How it works

- `scripts/sign-file.ps1` is a thin wrapper that signs one file (EXE/DLL/MSI/CAB) via jsign.
- The WiX projects call this wrapper from their signing targets when `SignOutput=true`:
  - `Setup/Setup.wixproj` -> `SignCabs`, `SignMsi`
  - `SetupBundle/SetupBundle.wixproj` -> `SignBundleEngine`, `SignBundle`
- `scripts/build-application.ps1 -Sign ...` sets `/p:SignOutput=true` and exports the signing
  configuration as `TSB_SIGN_*` environment variables, which the wrapper reads.

## Prerequisites

- [jsign](https://ebourg.github.io/jsign/) 5.0+ on `PATH` (e.g. `scoop install jsign`) or a path to `jsign.jar`
- Java 11+ (jsign is a Java tool)
- AWS credentials with `kms:Sign`, `kms:DescribeKey`, `kms:ListKeys` on the signing key
- The certificate chain (leaf + intermediates) as a single PEM/P7B file. Keep it under
  `signing/` (that folder and `*.pem` are git-ignored). The private key stays in KMS.
- The KMS key must be RSA with PKCS#1 v1.5 padding (jsign does not support PSS/raw RSA).

## Signing a build

Run everything in one PowerShell session so the AWS credentials are in scope:

```powershell
# 1. AWS credentials (jsign reads these via the wrapper; never commit them)
$env:AWS_ACCESS_KEY_ID     = "AKIA..."
$env:AWS_SECRET_ACCESS_KEY = "..."
# $env:AWS_SESSION_TOKEN   = "..."   # only for temporary STS credentials

# 2. Build signed MSIs + bundle for both platforms
.\scripts\build-application.ps1 -Configuration Release -Platforms x64,x86 -SkipTests -Local `
  -Sign -KmsRegion ap-south-1 -KmsKeyId alias/nw-ev-code-signing-cert-key `
  -CertFile .\signing\nw-ev-code-signing-cert.pem
```

### Signing parameters

| Parameter | Required | Description |
|---|---|---|
| `-Sign` | yes | Enables signing |
| `-KmsRegion` | yes | AWS region of the KMS key (e.g. `ap-south-1`) |
| `-KmsKeyId` | yes | KMS key id or alias (e.g. `alias/nw-ev-code-signing-cert-key`) |
| `-CertFile` | yes | Path to the certificate chain (`.pem`/`.p7b`) |
| `-TimestampUrl` | no | RFC 3161 TSA (default `http://timestamp.digicert.com`; GlobalSign: `http://timestamp.globalsign.com/tsa/r6advanced1`) |
| `-SignDescription` | no | Name embedded in the signature (default `Topin Secure Browser`) |
| `-JsignPath` | no | `jsign` on PATH or a path to `jsign.jar` |
| `-AwsCredentials` | no | `"accessKey|secretKey[|sessionToken]"`; prefer the `AWS_*` env vars instead |

## Quick sanity test (no full rebuild)

```powershell
$env:AWS_ACCESS_KEY_ID = "AKIA..."; $env:AWS_SECRET_ACCESS_KEY = "..."
$env:TSB_SIGN_KMS_REGION = "ap-south-1"
$env:TSB_SIGN_KMS_KEYID  = "alias/nw-ev-code-signing-cert-key"
$env:TSB_SIGN_CERTFILE   = (Resolve-Path .\signing\nw-ev-code-signing-cert.pem).Path

Copy-Item SafeExamBrowser.Runtime\bin\x64\Release\SafeExamBrowser.exe .\sigtest.exe
.\scripts\sign-file.ps1 -FilePath .\sigtest.exe
Get-AuthenticodeSignature .\sigtest.exe | Format-List Status, SignerCertificate
Remove-Item .\sigtest.exe
```

## Verify

```powershell
Get-AuthenticodeSignature .\Setup\bin\x64\Release\TSB.msi       | Format-List Status, StatusMessage
Get-AuthenticodeSignature .\Setup\bin\x86\Release\TSB.msi       | Format-List Status, StatusMessage
Get-AuthenticodeSignature .\SetupBundle\bin\x64\Release\TSB.exe | Format-List Status, StatusMessage
Get-AuthenticodeSignature .\SetupBundle\bin\x86\Release\TSB.exe | Format-List Status, StatusMessage
```

All should report `Status : Valid`. (`signtool verify /pa /v <file>` works too if the Windows
SDK signing tools are installed.)

## Publish the signed installer to S3

Once the signed bundles are produced, upload them with `scripts/upload-signed-to-s3.ps1`. The script
re-verifies the Authenticode signature of each platform bundle (so an unsigned binary can never be
published), then uploads to a clean, versioned layout. The S3 credentials are separate from the KMS
signing credentials and are passed in as parameters (never committed).

```powershell
# Uploads both x64 and x86 by default
.\scripts\upload-signed-to-s3.ps1 `
  -AccessKey "AKIA..." -SecretKey "..." `
  -Bucket topin-secure-browser -Channel beta -Region ap-south-1
```

This produces the following objects in the bucket:

| Object | Purpose |
|---|---|
| `beta/windows/tsb.exe` | Rolling "latest" pointer for x64 (stable download URL, `no-cache`) |
| `beta/windows/tsb_x86.exe` | Rolling "latest" pointer for x86 (`no-cache`) |
| `beta/windows/tsb_latest.json` | Latest metadata for all platforms: version, commit, SHA-256, size, timestamp |
| `beta/windows/<version>/tsb.exe` | Immutable versioned x64 archive (long-cache) |
| `beta/windows/<version>/tsb_x86.exe` | Immutable versioned x86 archive (long-cache) |

`<version>` is read from each EXE's product version. The primary (x64) download URL stays
`https://topin-secure-browser.s3.ap-south-1.amazonaws.com/beta/windows/tsb.exe`.

### Upload parameters

| Parameter | Required | Description |
|---|---|---|
| `-AccessKey` / `-SecretKey` | yes | AWS credentials with `s3:PutObject` on the bucket |
| `-SessionToken` | no | Only for temporary STS credentials |
| `-Bucket` | no | Target bucket (default `topin-secure-browser`) |
| `-Channel` | no | Release channel / prefix (default `beta`) |
| `-Region` | no | Bucket region (default `ap-south-1`) |
| `-Platforms` | no | Platforms to upload (default `x64, x86`); x64 -> `tsb.exe`, x86 -> `tsb_x86.exe` |
| `-Configuration` | no | Build configuration (default `Release`) |
| `-SkipSignatureCheck` | no | Upload even if the signature is not `Valid` (not recommended) |
| `-DryRun` | no | Print what would be uploaded without uploading |

Run a `-DryRun` first to confirm the resolved versions and S3 keys before publishing. To publish a
single platform, pass e.g. `-Platforms x64`.

## Implementation notes / gotchas

- **Credentials with `|`**: jsign's AWS store type only accepts credentials via `--storepass`
  (it does not read `AWS_*` itself). The wrapper builds that value from the `AWS_*` variables
  and passes it via jsign's `env:` prefix, so the `accessKey|secretKey` string never appears
  on the command line (a cmd.exe-based jsign shim would otherwise split it at the `|`).
- **Transient file locks**: the wrapper retries with backoff. The freshly written ~320 MB
  bundle is often briefly locked by antivirus right after WiX reattaches the burn engine; the
  retry lets the lock clear. Persistent locks: exclude the output folder in Defender.
- **SmartScreen**: a valid signature is separate from SmartScreen reputation. A newly issued
  certificate starts with no reputation, so early downloads may show a warning until
  reputation accrues. Submitting the binary at the Microsoft Defender SmartScreen submission
  portal can speed this up. This is not a build/signing defect.
