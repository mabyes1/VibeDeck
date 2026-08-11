#Requires -Version 5.1
<#
.SYNOPSIS
  Publish VibeDeck Host and build the canonical Windows Setup installer.

.DESCRIPTION
  1. dotnet publish self-contained win-x64 Host
  2. Stage payload under artifacts/windows-setup/payload
  3. Compile Inno Setup script to VibeDeck-Setup-<version>.exe

.PARAMETER Configuration
  Build configuration (default Release).

.PARAMETER Version
  Product version stamped into Host and installer. Defaults to the csproj Version.

.PARAMETER SkipInno
  Only publish payload; do not compile Setup.exe.

.PARAMETER InstallInno
  If ISCC is missing, install Inno Setup 6 via winget.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version,
    [switch]$SkipInno,
    [switch]$InstallInno,
    [switch]$SkipTests,
    # Optional path to the OFFLINE release-signing private key (PEM). When given,
    # scripts\sign-release.ps1 is invoked to produce the .sig update-integrity
    # signature next to Setup.exe. See ProductUpdateOptions.RequireSignedUpdates.
    [string]$SigningKeyPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\VibeDeck.Host\VibeDeck.Host.csproj"
$packagingDir = Join-Path $repoRoot "packaging\windows-setup"
$artifactRoot = Join-Path $repoRoot "artifacts\windows-setup"
$payloadRoot = Join-Path $artifactRoot "payload"
$issPath = Join-Path $packagingDir "VibeDeck.iss"
$iconPath = Join-Path $packagingDir "vibedeck.ico"
$cloudflaredVersion = "2026.7.2"
$cloudflaredSha256 = "CDB5D4432F6AE1595654A692A51308B69D2BF7AF961F5578D9391837CF072DF9"
$cloudflaredUrl = "https://github.com/cloudflare/cloudflared/releases/download/$cloudflaredVersion/cloudflared-windows-amd64.exe"
$cloudflaredLicenseSha256 = "58D1E17FFE5109A7AE296CAAFCADFDBE6A7D176F0BC4AB01E12A689B0499D8BD"
$cloudflaredLicenseUrl = "https://raw.githubusercontent.com/cloudflare/cloudflared/$cloudflaredVersion/LICENSE"
$dependencyRoot = Join-Path $artifactRoot "dependencies"
$cloudflaredCache = Join-Path $dependencyRoot "cloudflared-$cloudflaredVersion-windows-amd64.exe"
$cloudflaredLicenseCache = Join-Path $dependencyRoot "cloudflared-$cloudflaredVersion-LICENSE.txt"

function Write-Step([string]$message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Remove-DirectorySafely([string]$path) {
    $resolved = [System.IO.Path]::GetFullPath($path)
    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the repository: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 7\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path -LiteralPath $path)) {
            return [string]$path
        }
    }

    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return [string]$cmd.Source
    }

    return $null
}

function Ensure-Icon {
    if (Test-Path -LiteralPath $iconPath) {
        return
    }
    throw "Missing canonical installer icon: $iconPath"
}

function Ensure-Iscc {
    $iscc = Find-Iscc
    if ($iscc) {
        return $iscc
    }

    if (-not $InstallInno) {
        Write-Warning "Inno Setup compiler (ISCC.exe) not found."
        Write-Warning "Install with: winget install JRSoftware.InnoSetup"
        Write-Warning "Or re-run: scripts\package-windows-setup.ps1 -InstallInno"
        return $null
    }

    Write-Step "Installing Inno Setup 6 via winget"
    # Winget writes progress to the success stream; discard so it is not returned from this function.
    & winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements | Out-Host
    $iscc = Find-Iscc
    if (-not $iscc) {
        throw "Inno Setup installed but ISCC.exe still not found. Open a new shell and re-run."
    }
    return [string]$iscc
}

function Normalize-Version([string]$value) {
    if ($value -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must look like 0.1.0 (got '$value')"
    }
    return $value
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Host project not found: $project"
}
if (-not (Test-Path -LiteralPath $issPath)) {
    throw "Inno script not found: $issPath"
}

Ensure-Icon

if (-not $SkipTests) {
    Write-Step "Running product source checks"
    & (Join-Path $PSScriptRoot "test-product-flow.ps1") -Source
    if ($LASTEXITCODE -ne 0) { throw "Product source checks failed." }
}

function Get-VerifiedDownload([string]$url, [string]$path, [string]$expectedSha256, [string]$description) {
    if (Test-Path -LiteralPath $path) {
        $cachedHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($cachedHash -eq $expectedSha256) {
            return $path
        }
        Remove-Item -LiteralPath $path -Force
    }

    Write-Step "Downloading verified $description"
    New-Item -ItemType Directory -Path $dependencyRoot -Force | Out-Null
    $temporaryPath = $path + ".download"
    try {
        Invoke-WebRequest -Uri $url -OutFile $temporaryPath -UseBasicParsing
        $downloadedHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
        if ($downloadedHash -ne $expectedSha256) {
            throw "$description checksum mismatch. Expected $expectedSha256, got $downloadedHash."
        }
        Move-Item -LiteralPath $temporaryPath -Destination $path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
    return $path
}
$projectXml = [xml][IO.File]::ReadAllText($project, [Text.Encoding]::UTF8)
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$projectXml.Project.PropertyGroup.Version
}
$Version = Normalize-Version $Version

Write-Step "Publishing VibeDeck Host (self-contained win-x64)"
Remove-DirectorySafely $payloadRoot
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $payloadRoot

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Product marker forces ProgramData data paths + installed layout detection
Copy-Item -LiteralPath (Join-Path $packagingDir "product-install.json") -Destination $payloadRoot -Force
if (Test-Path -LiteralPath $iconPath) {
    Copy-Item -LiteralPath $iconPath -Destination $payloadRoot -Force
}
$cloudflared = Get-VerifiedDownload $cloudflaredUrl $cloudflaredCache $cloudflaredSha256 "Cloudflare connector $cloudflaredVersion"
$cloudflaredLicense = Get-VerifiedDownload $cloudflaredLicenseUrl $cloudflaredLicenseCache $cloudflaredLicenseSha256 "cloudflared license $cloudflaredVersion"
$cloudflaredSignature = Get-AuthenticodeSignature -FilePath $cloudflared
if ($cloudflaredSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    $cloudflaredSignature.SignerCertificate.Subject -notmatch 'O="?Cloudflare, Inc\."?') {
    throw "cloudflared Authenticode signature is not a valid Cloudflare, Inc. signature."
}
$connectorDirectory = Join-Path $payloadRoot "connectors"
New-Item -ItemType Directory -Path $connectorDirectory -Force | Out-Null
Copy-Item -LiteralPath $cloudflared -Destination (Join-Path $connectorDirectory "cloudflared.exe") -Force
$licenseDirectory = Join-Path $payloadRoot "licenses"
New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
Copy-Item -LiteralPath $cloudflaredLicense -Destination (Join-Path $licenseDirectory "cloudflared-LICENSE.txt") -Force

$hostExe = Join-Path $payloadRoot "VibeDeck.Host.exe"
if (-not (Test-Path -LiteralPath $hostExe)) {
    throw "Publish output missing VibeDeck.Host.exe"
}

$wwwroot = Join-Path $payloadRoot "wwwroot"
if (-not (Test-Path -LiteralPath $wwwroot)) {
    throw "Publish output missing wwwroot"
}

& (Join-Path $PSScriptRoot "test-product-flow.ps1") -Payload -PayloadPath $payloadRoot
if ($LASTEXITCODE -ne 0) { throw "Published payload checks failed." }

Write-Host "Payload ready: $payloadRoot"

if ($SkipInno) {
    Write-Host "SkipInno set — Setup.exe not built."
    Write-Host "Install manually with an elevated shell:"
    Write-Host "  scripts\install-windows-product.ps1"
    exit 0
}

$iscc = Ensure-Iscc
if (-not $iscc) {
    Write-Host ""
    Write-Host "Payload published. Install Inno Setup to build Setup.exe, then re-run this script."
    Write-Host "  winget install JRSoftware.InnoSetup"
    Write-Host "  scripts\package-windows-setup.ps1 -Version $Version"
    exit 0
}

Write-Step "Compiling Setup with Inno Setup"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

# Inno #define paths must be absolute or relative to the .iss file location
$payloadForIss = $payloadRoot
$outputForIss = $artifactRoot

& $iscc `
    "/DMyAppVersion=$Version" `
    "/DMyPayloadDir=$payloadForIss" `
    "/DMyOutputDir=$outputForIss" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$setup = Get-ChildItem -LiteralPath $artifactRoot -Filter "VibeDeck-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $setup) {
    throw "Setup.exe was not produced under $artifactRoot"
}

# Update-integrity assets: the host updater downloads <setup>.sha256 (integrity)
# and, when published, <setup>.sig (authenticity, offline key) next to Setup.exe.
Write-Step "Writing update checksum"
$setupHash = (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$($setup.FullName).sha256", "$setupHash  $($setup.Name)`n")
Write-Host "  $($setup.FullName).sha256"

if ($SigningKeyPath) {
    Write-Step "Signing release (detached update-integrity signature)"
    # sign-release.ps1 requires PowerShell 7+ and throws on failure
    # ($ErrorActionPreference = Stop propagates it here).
    & (Join-Path $PSScriptRoot "sign-release.ps1") -Sign -KeyPath $SigningKeyPath -InstallerPath $setup.FullName
} else {
    Write-Host ""
    Write-Host "NOTE: no -SigningKeyPath given; Setup.exe has no detached release signature." -ForegroundColor Yellow
    Write-Host "      Sign before publishing with: scripts\sign-release.ps1 -Sign -KeyPath <offline-key> -InstallerPath `"$($setup.FullName)`"" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "VibeDeck Setup ready:" -ForegroundColor Green
Write-Host "  $($setup.FullName)"
Write-Host ""
Write-Host "After install:"
Write-Host "  - Desktop / Start Menu icon opens the web PC UI (http://127.0.0.1:5000)"
Write-Host "  - VibeDeck Host auto-starts in the signed-in desktop session"
Write-Host "  - Data: $env:ProgramData\VibeDeck"
