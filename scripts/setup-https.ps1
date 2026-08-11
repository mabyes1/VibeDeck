[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$certDir = Join-Path $env:LOCALAPPDATA "VibeDeck\certs"
$rootPfxPath = Join-Path $certDir "vibedeck-root.pfx"
$rootCerPath = Join-Path $certDir "vibedeck-root.cer"
$hostCerPath = Join-Path $certDir "vibedeck-host.cer"
$hostPfxPath = Join-Path $certDir "vibedeck-host.pfx"
$statePath = Join-Path $certDir "vibedeck-certificate-state.json"

function New-RandomSerial {
    $serial = New-Object byte[] 16
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($serial)
    }
    finally {
        $rng.Dispose()
    }
    return $serial
}

function Get-VibeDeckLanIpAddresses {
    try {
        return @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object {
                $_.IPAddress -and
                $_.IPAddress -ne "127.0.0.1" -and
                -not $_.IPAddress.StartsWith("169.254.")
            } |
            Select-Object -ExpandProperty IPAddress -Unique)
    }
    catch {
        return @()
    }
}

if ((Test-Path $rootPfxPath) -and (Test-Path $hostPfxPath) -and (Test-Path $rootCerPath) -and (Test-Path $hostCerPath) -and (Test-Path $statePath) -and -not $Force) {
    if (-not $Quiet) {
        Write-Host "VibeDeck HTTPS certificate already exists."
        Write-Host "Root PFX : $rootPfxPath"
        Write-Host "Host PFX : $hostPfxPath"
        Write-Host "Root CER : $rootCerPath"
        Write-Host "Host startup auto-refreshes the Host certificate if this PC's LAN IP changes."
    }
    exit 0
}

New-Item -ItemType Directory -Path $certDir -Force | Out-Null

$hostName = [System.Net.Dns]::GetHostName()
$dnsNames = @("localhost", $hostName, "$hostName.local") | Select-Object -Unique
$ipAddresses = @("127.0.0.1") + (Get-VibeDeckLanIpAddresses)
$ipAddresses = $ipAddresses | Select-Object -Unique
$notBefore = [System.DateTimeOffset]::UtcNow.AddMinutes(-5)
$rootNotAfter = $notBefore.AddYears(10)
$hostNotAfter = $notBefore.AddYears(2)

$rootKey = [System.Security.Cryptography.RSA]::Create(4096)
$hostKey = [System.Security.Cryptography.RSA]::Create(2048)

try {
    $rootRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=VibeDeck Local Root CA",
        $rootKey,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $rootRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
    $rootRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign,
            $true))
    $rootRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($rootRequest.PublicKey, $false))
    $rootCertificate = $rootRequest.CreateSelfSigned($notBefore, $rootNotAfter)

    $hostRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=VibeDeck Local Host",
        $hostKey,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $hostRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $hostRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment,
            $true))
    $oids = [System.Security.Cryptography.OidCollection]::new()
    $oids.Add([System.Security.Cryptography.Oid]::new("1.3.6.1.5.5.7.3.1")) | Out-Null
    $hostRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $false))

    $san = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    foreach ($dnsName in $dnsNames) {
        $san.AddDnsName($dnsName)
    }
    foreach ($ipAddress in $ipAddresses) {
        $san.AddIpAddress([System.Net.IPAddress]::Parse($ipAddress))
    }
    $hostRequest.CertificateExtensions.Add($san.Build($false))
    $hostRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($hostRequest.PublicKey, $false))

    $hostCertificateWithoutKey = $hostRequest.Create($rootCertificate, $notBefore, $hostNotAfter, (New-RandomSerial))
    $hostCertificate = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey(
        $hostCertificateWithoutKey,
        $hostKey)

    [System.IO.File]::WriteAllBytes(
        $rootCerPath,
        $rootCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    [System.IO.File]::WriteAllBytes(
        $rootPfxPath,
        $rootCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, ""))
    [System.IO.File]::WriteAllBytes(
        $hostCerPath,
        $hostCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    [System.IO.File]::WriteAllBytes(
        $hostPfxPath,
        $hostCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, ""))

    $state = [ordered]@{
        CreatedAtUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        DnsNames = @($dnsNames)
        IpAddresses = @($ipAddresses)
        RootThumbprint = $rootCertificate.Thumbprint
        HostThumbprint = $hostCertificate.Thumbprint
        HostNotAfterUtc = $hostCertificate.NotAfter.ToUniversalTime().ToString("O")
    }
    $state | ConvertTo-Json -Depth 4 | Set-Content -Path $statePath -Encoding UTF8
}
finally {
    if ($rootCertificate) { $rootCertificate.Dispose() }
    if ($hostCertificateWithoutKey) { $hostCertificateWithoutKey.Dispose() }
    if ($hostCertificate) { $hostCertificate.Dispose() }
    $rootKey.Dispose()
    $hostKey.Dispose()
}

if (-not $Quiet) {
    Write-Host "VibeDeck HTTPS certificate created."
    Write-Host "Root PFX : $rootPfxPath"
    Write-Host "Host PFX : $hostPfxPath"
    Write-Host "Root CER : $rootCerPath"
    Write-Host "Host CER : $hostCerPath"
    Write-Host "State    : $statePath"
    Write-Host ""
    Write-Host "Restart VibeDeck Host. It will serve HTTPS on https://0.0.0.0:5443 when the PFX exists."
    Write-Host "Install and trust the Root CER on the phone before opening the HTTPS URL."
    Write-Host "Host startup auto-refreshes the Host certificate if this PC's LAN IP changes."
}
