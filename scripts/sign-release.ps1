#Requires -Version 7.0
<#
.SYNOPSIS
  Offline release signing for VibeDeck update integrity.

.DESCRIPTION
  Produces the detached ECDSA P-256 release signature that
  ProductUpdateService verifies against the public key pinned in
  src/VibeDeck.Host/Updates/ProductUpdateOptions.cs.

  Signature format: ECDSA P-256 over the SHA-256 hash of the installer,
  IEEE P1363 (r||s) encoding, stored base64 in <installer>.sig.
  Also writes <installer>.sha256 ("<HASH>  <filename>").

  KEEP THE PRIVATE KEY OFFLINE. It must never live in the repository, in CI
  secrets, or on the update origin - the entire point is that a compromised
  GitHub account/CI cannot produce a valid signature.

.EXAMPLE
  # One-time: generate the keypair (store the .pem on an offline medium)
  pwsh scripts/sign-release.ps1 -GenerateKey -KeyPath D:\secure\vibedeck-release-signing.pem

  # Paste the printed PUBLIC key into ProductUpdateOptions.PinnedReleaseSigningPublicKeyPem,
  # then set RequireSignedUpdatesDefault = true (after at least one signed release is out).

.EXAMPLE
  # Per release: sign the built installer, then upload .exe, .exe.sha256 and .exe.sig
  pwsh scripts/sign-release.ps1 -Sign -KeyPath D:\secure\vibedeck-release-signing.pem `
      -InstallerPath artifacts\windows-setup\VibeDeck-Setup-0.1.19.exe

.EXAMPLE
  # Sanity-check a signature with only the public key
  pwsh scripts/sign-release.ps1 -Verify -PublicKeyPath vibedeck-release-signing.pub.pem `
      -InstallerPath artifacts\windows-setup\VibeDeck-Setup-0.1.19.exe
#>
[CmdletBinding(DefaultParameterSetName = "Sign")]
param(
    [Parameter(ParameterSetName = "GenerateKey", Mandatory)]
    [switch]$GenerateKey,

    [Parameter(ParameterSetName = "Sign", Mandatory)]
    [switch]$Sign,

    [Parameter(ParameterSetName = "Verify", Mandatory)]
    [switch]$Verify,

    [Parameter(ParameterSetName = "GenerateKey", Mandatory)]
    [Parameter(ParameterSetName = "Sign", Mandatory)]
    [string]$KeyPath,

    [Parameter(ParameterSetName = "Verify", Mandatory)]
    [string]$PublicKeyPath,

    [Parameter(ParameterSetName = "Sign", Mandatory)]
    [Parameter(ParameterSetName = "Verify", Mandatory)]
    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"

function Get-InstallerSha256Hex([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

switch ($PSCmdlet.ParameterSetName) {
    "GenerateKey" {
        if (Test-Path -LiteralPath $KeyPath) {
            throw "Refusing to overwrite existing key: $KeyPath"
        }
        $directory = Split-Path -Parent ([System.IO.Path]::GetFullPath($KeyPath))
        if ($directory -and -not (Test-Path -LiteralPath $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        $ecdsa = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName("nistP256"))
        try {
            $privatePem = $ecdsa.ExportPkcs8PrivateKeyPem()
            $publicPem = $ecdsa.ExportSubjectPublicKeyInfoPem()
        }
        finally {
            $ecdsa.Dispose()
        }

        [System.IO.File]::WriteAllText($KeyPath, $privatePem + "`n")
        $publicPath = [System.IO.Path]::ChangeExtension($KeyPath, $null).TrimEnd('.') + ".pub.pem"
        [System.IO.File]::WriteAllText($publicPath, $publicPem + "`n")

        Write-Host "Private key written to: $KeyPath" -ForegroundColor Yellow
        Write-Host "  -> Move it to offline storage. Do NOT commit, do NOT put in CI." -ForegroundColor Yellow
        Write-Host "Public key written to:  $publicPath"
        Write-Host ""
        Write-Host "Pin this public key in src/VibeDeck.Host/Updates/ProductUpdateOptions.cs" -ForegroundColor Cyan
        Write-Host "(PinnedReleaseSigningPublicKeyPem), then flip RequireSignedUpdatesDefault to true" -ForegroundColor Cyan
        Write-Host "AFTER at least one signed release has shipped:" -ForegroundColor Cyan
        Write-Host ""
        Write-Host $publicPem
    }

    "Sign" {
        if (-not (Test-Path -LiteralPath $KeyPath)) { throw "Key not found: $KeyPath" }
        if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer not found: $InstallerPath" }

        $hashHex = Get-InstallerSha256Hex $InstallerPath
        $hashBytes = [System.Convert]::FromHexString($hashHex)

        $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
        try {
            $ecdsa.ImportFromPem((Get-Content -LiteralPath $KeyPath -Raw))
            $signature = $ecdsa.SignHash($hashBytes)  # IEEE P1363 (r||s), matches InstallerVerifier.VerifyDetachedSignature
        }
        finally {
            $ecdsa.Dispose()
        }

        $fileName = Split-Path -Leaf $InstallerPath
        $sha256Path = "$InstallerPath.sha256"
        $sigPath = "$InstallerPath.sig"
        [System.IO.File]::WriteAllText($sha256Path, "$hashHex  $fileName`n")
        [System.IO.File]::WriteAllText($sigPath, [System.Convert]::ToBase64String($signature) + "`n")

        Write-Host "SHA-256:   $hashHex"
        Write-Host "Checksum:  $sha256Path"
        Write-Host "Signature: $sigPath"
        Write-Host ""
        Write-Host "Upload ALL THREE release assets: the .exe, the .exe.sha256 and the .exe.sig" -ForegroundColor Green
    }

    "Verify" {
        if (-not (Test-Path -LiteralPath $PublicKeyPath)) { throw "Public key not found: $PublicKeyPath" }
        if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer not found: $InstallerPath" }
        $sigPath = "$InstallerPath.sig"
        if (-not (Test-Path -LiteralPath $sigPath)) { throw "Signature not found: $sigPath" }

        $hashBytes = [System.Convert]::FromHexString((Get-InstallerSha256Hex $InstallerPath))
        $signature = [System.Convert]::FromBase64String((Get-Content -LiteralPath $sigPath -Raw).Trim())

        $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
        try {
            $ecdsa.ImportFromPem((Get-Content -LiteralPath $PublicKeyPath -Raw))
            $valid = $ecdsa.VerifyHash($hashBytes, $signature)
        }
        finally {
            $ecdsa.Dispose()
        }

        if ($valid) {
            Write-Host "Signature VALID for $InstallerPath" -ForegroundColor Green
            exit 0
        }
        Write-Host "Signature INVALID for $InstallerPath" -ForegroundColor Red
        exit 1
    }
}
