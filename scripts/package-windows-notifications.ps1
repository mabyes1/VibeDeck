[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Install,
    [switch]$Uninstall,
    [switch]$RegisterOnly,
    [switch]$InstallCertificateMachine,
    [string]$PackageVersion = "1.0.0.0",
    [string]$CertificatePassword = "VibeDeckDev"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\VibeDeck.Host\VibeDeck.Host.csproj"
$manifestTemplate = Join-Path $repoRoot "packaging\windows-notifications\Package.appxmanifest"
$iconSource = Join-Path $repoRoot "packaging\windows-setup\vibedeck.ico"
$artifactRoot = Join-Path $repoRoot "artifacts\windows-notifications"
$publishRoot = Join-Path $artifactRoot "publish"
$packageRoot = Join-Path $artifactRoot "package"
$msixPath = Join-Path $artifactRoot "VibeDeck.WindowsNotifications.msix"
$pfxPath = Join-Path $artifactRoot "VibeDeck.Dev.pfx"
$cerPath = Join-Path $artifactRoot "VibeDeck.Dev.cer"
$manifestPath = Join-Path $packageRoot "AppxManifest.xml"
$localDotNet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotNet) { $localDotNet } else { "dotnet" }

function Remove-DirectorySafely([string]$path) {
    $resolved = [System.IO.Path]::GetFullPath($path)
    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the repository: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

function Find-WindowsKitTool([string]$name) {
    $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter $name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "x64" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $tool) { throw "$name was not found. Install the Windows 10/11 SDK." }
    return $tool.FullName
}

function Ensure-DeveloperCertificate {
    $certificate = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq "CN=VibeDeck Dev" -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if (-not $certificate) {
        $certificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject "CN=VibeDeck Dev" `
            -FriendlyName "VibeDeck Dev" `
            -KeyUsage DigitalSignature `
            -CertStoreLocation Cert:\CurrentUser\My
    }

    $securePassword = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
    Export-Certificate -Cert $certificate -FilePath $cerPath -Force | Out-Null
    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword -Force | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople -ErrorAction SilentlyContinue | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\Root -ErrorAction SilentlyContinue | Out-Null
    return $certificate
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-MachineCertificateTrust([string]$path) {
    if (-not (Test-IsAdministrator)) {
        throw "InstallCertificateMachine requires an elevated PowerShell window. Open PowerShell as Administrator and rerun this script with -InstallCertificateMachine -RegisterOnly."
    }
    Import-Certificate -FilePath $path -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
}

function Resolve-PackageVersion {
    try { $version = [version]$PackageVersion }
    catch { throw "PackageVersion must be a four-part version such as 1.0.0.0." }

    $installed = Get-AppxPackage -Name "VibeDeck.Dev" -ErrorAction SilentlyContinue |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if ($installed) {
        $installedVersion = [version]$installed.Version
        if ($version -le $installedVersion) {
            $version = [version]::new(
                $installedVersion.Major,
                $installedVersion.Minor,
                $installedVersion.Build,
                $installedVersion.Revision + 1)
        }
    }
    return $version.ToString(4)
}

function Assert-GuiExecutable([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $peOffset = [System.BitConverter]::ToInt32($bytes, 0x3c)
    $optionalHeaderOffset = $peOffset + 24
    $magic = [System.BitConverter]::ToUInt16($bytes, $optionalHeaderOffset)
    if ($magic -notin 0x10b, 0x20b) {
        throw "Notification companion is not a valid Windows executable: $path"
    }

    $subsystem = [System.BitConverter]::ToUInt16($bytes, $optionalHeaderOffset + 68)
    if ($subsystem -ne 2) {
        throw "Notification companion must be a Windows GUI executable, but subsystem $subsystem was built: $path"
    }
}

if ($Uninstall) {
    $package = Get-AppxPackage -Name "VibeDeck.Dev" -ErrorAction SilentlyContinue
    if ($package) {
        Remove-AppxPackage -Package $package.PackageFullName
        Write-Host "VibeDeck Notifications identity package removed."
    } else {
        Write-Host "VibeDeck Notifications identity package is not installed."
    }
    Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "VibeDeckNotifications" -ErrorAction SilentlyContinue
    exit 0
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Remove-DirectorySafely $publishRoot
Remove-DirectorySafely $packageRoot
if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }
New-Item -ItemType Directory -Force -Path $publishRoot, $packageRoot, (Join-Path $packageRoot "Assets") | Out-Null

& $dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:UseAppHost=true -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
Copy-Item -LiteralPath $manifestTemplate -Destination $manifestPath -Force
$resolvedPackageVersion = Resolve-PackageVersion
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$manifestContent = [System.IO.File]::ReadAllText($manifestPath, $utf8NoBom)
$identityMatch = [regex]::Match($manifestContent, '<Identity\b[^>]*>')
if (-not $identityMatch.Success) { throw "The package manifest does not contain an Identity element." }
$identity = $identityMatch.Value
$updatedIdentity = $identity -replace 'Version="[^"]+"', ('Version="{0}"' -f $resolvedPackageVersion)
$manifestContent = $manifestContent.Replace($identity, $updatedIdentity)
[System.IO.File]::WriteAllText($manifestPath, $manifestContent, $utf8NoBom)
if (-not (Test-Path -LiteralPath $iconSource)) { throw "Canonical VibeDeck icon is missing: $iconSource" }
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::new($iconSource, 192, 192)
$bitmap = $icon.ToBitmap()
try {
    $bitmap.Save((Join-Path $packageRoot "Assets\icon-192.png"), [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $bitmap.Dispose()
    $icon.Dispose()
}
Get-ChildItem -LiteralPath $publishRoot -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $packageRoot -Recurse -Force
}
foreach ($relative in @("wwwroot", "Installers", "Sideboard")) {
    $unused = Join-Path $packageRoot $relative
    if (Test-Path -LiteralPath $unused) { Remove-Item -LiteralPath $unused -Recurse -Force }
}
foreach ($pattern in @("appsettings*.json", "web.config", "*.pdb")) {
    Get-ChildItem -LiteralPath $packageRoot -File -Filter $pattern -ErrorAction SilentlyContinue | Remove-Item -Force
}
$packagedHostExe = Join-Path $packageRoot "VibeDeck.Host.exe"
$companionExe = Join-Path $packageRoot "VibeDeck.Notifications.exe"
if (-not (Test-Path -LiteralPath $packagedHostExe)) { throw "Published notification companion apphost is missing." }
Move-Item -LiteralPath $packagedHostExe -Destination $companionExe -Force
if (Test-Path -LiteralPath (Join-Path $packageRoot "wwwroot")) { throw "Notification package still contains the Host web application." }
if (Test-Path -LiteralPath (Join-Path $packageRoot "Installers")) { throw "Notification package still contains product installers." }
if (-not (Test-Path -LiteralPath $companionExe)) { throw "Notification companion executable is missing after packaging." }
Assert-GuiExecutable $companionExe

$makeAppx = Find-WindowsKitTool "makeappx.exe"
$signtool = Find-WindowsKitTool "signtool.exe"
& $makeAppx pack /d $packageRoot /p $msixPath /o
$null = Ensure-DeveloperCertificate
& $signtool sign /fd SHA256 /f $pfxPath /p $CertificatePassword $msixPath

if ($RegisterOnly -or $Install) {
    if ($InstallCertificateMachine) {
        Ensure-MachineCertificateTrust $cerPath
    }

    try {
        Add-AppxPackage -Path $msixPath -ForceApplicationShutdown
    } catch {
        $details = $_.Exception.Message
        if ($details -match "0x800B0109|0x80073CF0|certificate chain|root must be trusted|信任") {
            throw "MSIX 已建立並簽署，但 Windows 部署服務不信任開發憑證。請以系統管理員 PowerShell 重新執行：.\scripts\package-windows-notifications.ps1 -RegisterOnly -InstallCertificateMachine。原始錯誤：$details"
        }
        throw
    }
    $package = Get-AppxPackage -Name "VibeDeck.Dev" | Select-Object -First 1
    if (-not $package) { throw "Package registration did not produce an installed package identity." }
    Write-Host "VibeDeck Notifications MSIX installed."
    Write-Host "Package identity: $($package.PackageFullName)"
    New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Force | Out-Null
    Set-ItemProperty `
        -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
        -Name "VibeDeckNotifications" `
        -Type String `
        -Value 'explorer.exe "vibedeck-notifications://start"'
    Write-Host "Notification Companion will start automatically at user sign-in."
    Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList '"vibedeck-notifications://start/"' -WindowStyle Hidden
    Write-Host "VibeDeck Notifications companion launched. Enable Windows notifications in the PC dashboard if needed."
} else {
    Write-Host "Created signed identity package (version $resolvedPackageVersion): $msixPath"
}
