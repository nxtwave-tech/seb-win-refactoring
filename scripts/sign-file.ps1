# Sign a single file (EXE/DLL/MSI/CAB) using jsign with an AWS KMS-held key.
#
# This wrapper is invoked by the WiX signing targets (SignCabs/SignMsi/SignBundleEngine/
# SignBundle) so the projects stay decoupled from the signing mechanism. All configuration
# is provided through environment variables, which are set by build-application.ps1 when it
# is run with -Sign:
#
#   TSB_SIGN_KMS_REGION   AWS region holding the KMS key (e.g. us-east-1)        [required]
#   TSB_SIGN_KMS_KEYID    KMS key id or alias (e.g. alias/nw-ev-code-signing)    [required]
#   TSB_SIGN_CERTFILE     Path to the certificate chain (.pem/.p7b/.cer)         [required]
#   TSB_SIGN_TSAURL       RFC 3161 timestamp server URL                          [optional]
#   TSB_SIGN_DESC         Description embedded in the signature                  [optional]
#   TSB_SIGN_JSIGN        'jsign' (on PATH) or a path to jsign.jar               [optional]
#   TSB_SIGN_AWS_CREDS    "accessKey|secretKey[|sessionToken]"                   [optional]
#
# AWS credentials: jsign's AWS KMS store type only accepts credentials via --storepass (or
# ECS/EC2 instance metadata) - it does NOT read the AWS_* environment variables itself. This
# wrapper bridges that gap: if TSB_SIGN_AWS_CREDS is not set, it builds the --storepass value
# from the standard AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY / AWS_SESSION_TOKEN variables.
#
# AWS KMS has no native Windows signing provider, so jsign (https://ebourg.github.io/jsign/)
# is used to call kms:Sign directly. Requires Java 11+ and jsign 5.0+.

param(
    [Parameter(Mandatory = $true)][string]$FilePath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $FilePath)) {
    throw "sign-file: file to sign not found: $FilePath"
}

$region   = $env:TSB_SIGN_KMS_REGION
$keyId    = $env:TSB_SIGN_KMS_KEYID
$certFile = $env:TSB_SIGN_CERTFILE
$tsaUrl   = if ($env:TSB_SIGN_TSAURL) { $env:TSB_SIGN_TSAURL } else { "http://timestamp.digicert.com" }
$desc     = if ($env:TSB_SIGN_DESC)   { $env:TSB_SIGN_DESC }   else { "Topin Secure Browser" }
$jsign    = if ($env:TSB_SIGN_JSIGN)  { $env:TSB_SIGN_JSIGN }  else { "jsign" }

if (-not $region)   { throw "sign-file: TSB_SIGN_KMS_REGION is not set." }
if (-not $keyId)    { throw "sign-file: TSB_SIGN_KMS_KEYID is not set." }
if (-not $certFile) { throw "sign-file: TSB_SIGN_CERTFILE is not set." }
if (-not (Test-Path -LiteralPath $certFile)) { throw "sign-file: certificate chain file not found: $certFile" }

$jsignArgs = @(
    "--storetype", "AWS",
    "--keystore",  $region,
    "--alias",     $keyId,
    "--certfile",  $certFile,
    "--tsaurl",    $tsaUrl,
    "--tsmode",    "RFC3161",
    "--alg",       "SHA-256",
    "--name",      $desc
)

# jsign's AWS KMS store type does NOT read the standard AWS environment variables; it only
# accepts credentials via --storepass ("accessKey|secretKey[|sessionToken]") or via ECS/EC2
# instance metadata. To keep credentials off the command line while still supporting the
# usual AWS_* environment variables, derive the --storepass value here.
$awsCreds = $env:TSB_SIGN_AWS_CREDS
if (-not $awsCreds -and $env:AWS_ACCESS_KEY_ID -and $env:AWS_SECRET_ACCESS_KEY) {
    $awsCreds = "$($env:AWS_ACCESS_KEY_ID)|$($env:AWS_SECRET_ACCESS_KEY)"
    if ($env:AWS_SESSION_TOKEN) { $awsCreds += "|$($env:AWS_SESSION_TOKEN)" }
}

if (-not $awsCreds) {
    throw "sign-file: no AWS credentials available. Set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY (and AWS_SESSION_TOKEN if using temporary credentials), or set TSB_SIGN_AWS_CREDS to 'accessKey|secretKey[|sessionToken]'."
}

# Pass the credentials through an environment variable using jsign's 'env:' prefix instead
# of placing the "accessKey|secretKey" value directly on the command line. jsign is often
# installed as a cmd.exe shim (e.g. via scoop), which would interpret the '|' separator as
# a pipe operator and split the secret. jsign reads the value straight from the environment.
$env:JSIGN_AWS_STOREPASS = $awsCreds
$jsignArgs += @("--storepass", "env:JSIGN_AWS_STOREPASS")

$jsignArgs += $FilePath

# jsign may be provided as a command on PATH or as a path to jsign.jar (run via java -jar).
if ($jsign -match '\.jar$') {
    $exe = "java"
    $exeArgs = @("-jar", $jsign) + $jsignArgs
} else {
    $exe = $jsign
    $exeArgs = $jsignArgs
}

Write-Host "sign-file: signing '$FilePath' via jsign (AWS KMS, region=$region, key=$keyId)" -ForegroundColor Cyan

# Retry on transient failures. Large freshly-written outputs (e.g. the ~320 MB bundle) are
# frequently locked momentarily by antivirus/real-time scanning or by a lagging filesystem
# handle release right after WiX reattaches the burn engine, which makes jsign fail with a
# sharing violation. A short backoff lets the lock clear.
$maxAttempts = 5
$attempt = 0
while ($true) {
    $attempt++
    & $exe @exeArgs
    if ($LASTEXITCODE -eq 0) { break }

    if ($attempt -ge $maxAttempts) {
        throw "sign-file: jsign failed with exit code $LASTEXITCODE while signing '$FilePath' (after $attempt attempts)."
    }

    $delay = 3 * $attempt
    Write-Host "sign-file: attempt $attempt failed (exit $LASTEXITCODE); the file may be temporarily locked (e.g. antivirus). Retrying in $delay s..." -ForegroundColor Yellow
    Start-Sleep -Seconds $delay
}

Write-Host "sign-file: successfully signed '$FilePath'" -ForegroundColor Green
