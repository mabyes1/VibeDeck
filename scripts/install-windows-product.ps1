#Requires -Version 5.1
<#
.SYNOPSIS
  Install VibeDeck from a published payload without Inno Setup (dev / fallback path).

.DESCRIPTION
  Copies artifacts/windows-setup/payload to Program Files\VibeDeck, registers the
  VibeDeck Host desktop-session auto-start, creates Start Menu + Desktop shortcuts
  that open the web UI, and starts the Host in the signed-in session.

.PARAMETER PayloadPath
  Path to published payload (default artifacts/windows-setup/payload).

.PARAMETER InstallDir
  Install directory (default Program Files\VibeDeck).

.PARAMETER SkipDesktopIcon
  Do not create a desktop shortcut.

.PARAMETER SkipAutostart
  Copy files only; do not register Host auto-start.
#>
[CmdletBinding()]
param(
    [string]$PayloadPath,
    [string]$InstallDir,
    [switch]$SkipDesktopIcon,
    [Alias("SkipService")]
    [switch]$SkipAutostart
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-VibeDeckDataUserSid {
    # Returns @{ Sid = <user-sid-or-null>; FromService = <bool> }
    # When the installer runs as SYSTEM (silent deploy), never treat SYSTEM as the
    # interactive user — resolve the signed-in desktop account instead.
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $sid = $identity.User.Value
    $serviceSids = @("S-1-5-18", "S-1-5-19", "S-1-5-20")
    if ($sid -notin $serviceSids) {
        return @{ Sid = $sid; FromService = $false }
    }

    # Win32_ComputerSystem.UserName identifies the user on the physical console.
    # Do not pick the first explorer.exe: on Fast User Switching / RDP machines
    # enumeration order can grant VibeDeck secrets to the wrong signed-in user.
    $ownerSid = $null
    $consoleUser = (Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).UserName
    if (-not [string]::IsNullOrWhiteSpace($consoleUser)) {
        try {
            $ownerSid = [System.Security.Principal.NTAccount]::new($consoleUser).
                Translate([System.Security.Principal.SecurityIdentifier]).Value
        }
        catch {
            $ownerSid = $null
        }
    }

    return @{ Sid = $ownerSid; FromService = $true }
}

function Invoke-IcaclsChecked {
    param(
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )
    & icacls.exe @ArgumentList | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (icacls exit $LASTEXITCODE): icacls $($ArgumentList -join ' ')"
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Run this script in an elevated PowerShell (Administrator)."
}

if (-not $PayloadPath) {
    $PayloadPath = Join-Path $repoRoot "artifacts\windows-setup\payload"
}
if (-not $InstallDir) {
    $InstallDir = Join-Path ${env:ProgramFiles} "VibeDeck"
}

$hostExeName = "VibeDeck.Host.exe"
$serviceName = "VibeDeckHost"
$runValueName = "VibeDeckHost"
$webUrl = "http://127.0.0.1:5000"

if (-not (Test-Path -LiteralPath (Join-Path $PayloadPath $hostExeName))) {
    throw "Payload missing $hostExeName. Run scripts\package-windows-setup.ps1 -SkipInno first. Path: $PayloadPath"
}

Write-Host "Installing VibeDeck → $InstallDir"

# Stop existing service if present
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq "Running") {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        try {
            $existing.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(12))
        } catch {
            $serviceProcess = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
            if ($serviceProcess -and $serviceProcess.ProcessId -gt 0) {
                Write-Host "Service did not stop in time; terminating VibeDeckHost PID $($serviceProcess.ProcessId)."
                Stop-Process -Id $serviceProcess.ProcessId -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 1
            }
        }
    }
    & sc.exe delete $serviceName | Out-Null
    Write-Host "Removed legacy Session 0 service $serviceName."
}

function Remove-VibeDeckFirewallRule {
    Get-NetFirewallRule -DisplayName "VibeDeck Host" -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
}

function Set-VibeDeckFirewallRule {
    param([Parameter(Mandatory = $true)][string]$ProgramPath)

    Remove-VibeDeckFirewallRule
    New-NetFirewallRule `
        -DisplayName "VibeDeck Host" `
        -Description "Allow paired phones to reach VibeDeck Host" `
        -Direction Inbound `
        -Action Allow `
        -Program $ProgramPath `
        -Profile Any `
        -Enabled True | Out-Null
}

function Clear-InstallDirectory([string]$path) {
    $resolved = [IO.Path]::GetFullPath($path).TrimEnd('\')
    $programFilesRoot = [IO.Path]::GetFullPath(${env:ProgramFiles}).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($programFilesRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear an install directory outside Program Files: $resolved"
    }

    $lastError = $null
    foreach ($attempt in 1..8) {
        try {
            Get-ChildItem -LiteralPath $resolved -Force -ErrorAction Stop |
                Remove-Item -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds (200 * $attempt)
        }
    }

    throw "Could not replace VibeDeck application files after 8 attempts: $($lastError.Exception.Message)"
}

# Stop an existing desktop-session Host from this install directory before
# replacing files. The packaged Windows notification companion has a different
# path and must remain running.
foreach ($processName in @($hostExeName)) {
    Get-CimInstance Win32_Process -Filter "Name='$processName'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}
Start-Sleep -Milliseconds 500

if (Test-Path -LiteralPath $InstallDir) {
    $hasKnownInstall = (Test-Path -LiteralPath (Join-Path $InstallDir $hostExeName)) -or
        (Test-Path -LiteralPath (Join-Path $InstallDir "product-install.json"))
    $hasFiles = $null -ne (Get-ChildItem -LiteralPath $InstallDir -Force | Select-Object -First 1)
    if ($hasFiles -and -not $hasKnownInstall) {
        throw "Refusing to replace non-VibeDeck directory: $InstallDir"
    }
    # Product data lives in ProgramData. Clearing the replaceable app directory
    # prevents removed web modules or old launchers from surviving an update.
    Clear-InstallDirectory $InstallDir
}
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path (Join-Path $PayloadPath "*") -Destination $InstallDir -Recurse -Force

$productData = Join-Path $env:ProgramData "VibeDeck"
# Match Setup's HardenDataDirectoryAcl (VibeDeck.iss): SYSTEM + Administrators
# (full) + signed-in desktop user (modify). Never grant BUILTIN\Users.
# Every icacls invocation is checked; any failure aborts the install.
New-Item -ItemType Directory -Path $productData -Force | Out-Null
$userInfo = Get-VibeDeckDataUserSid
$userGrant = if ($userInfo.Sid) {
    "*{0}:(OI)(CI)M" -f $userInfo.Sid
}
else {
    # Silent deploy from SYSTEM without a resolvable interactive user.
    "*S-1-5-4:(OI)(CI)M"
}
Write-Host "[install] ProgramData user grant: $userGrant (fromService=$($userInfo.FromService))"

Invoke-IcaclsChecked -ArgumentList @(
    $productData,
    "/inheritance:r",
    "/grant:r", "*S-1-5-18:(OI)(CI)F",
    "/grant:r", "*S-1-5-32-544:(OI)(CI)F",
    "/grant:r", $userGrant
) -FailureMessage "Could not harden ACLs on $productData"

$staleRemovals = @("/remove:g", "*S-1-5-32-545", "/remove:g", "*S-1-1-0", "/remove:g", "*S-1-5-11")
if ($userInfo.Sid) {
    $staleRemovals += @("/remove:g", "*S-1-5-4")
}
Invoke-IcaclsChecked -ArgumentList (@($productData) + $staleRemovals) `
    -FailureMessage "Could not remove stale Users/Everyone/Authenticated Users ACEs on $productData"

Invoke-IcaclsChecked -ArgumentList @("$productData\*", "/reset", "/T", "/C", "/Q") `
    -FailureMessage "Could not reset child ACLs under $productData"

$iconPath = Join-Path $InstallDir "vibedeck.ico"
$hostExe = Join-Path $InstallDir $hostExeName

# Shortcuts
$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\VibeDeck"
New-Item -ItemType Directory -Path $startMenu -Force | Out-Null

$startShortcut = $shell.CreateShortcut((Join-Path $startMenu "VibeDeck.lnk"))
$startShortcut.TargetPath = $hostExe
$startShortcut.Arguments = "--open"
$startShortcut.WorkingDirectory = $InstallDir
$startShortcut.WindowStyle = 7
$startShortcut.Description = "Open VibeDeck on this PC"
if (Test-Path -LiteralPath $iconPath) {
    $startShortcut.IconLocation = "$iconPath,0"
}
$startShortcut.Save()

if (-not $SkipDesktopIcon) {
    $desktop = [Environment]::GetFolderPath("CommonDesktopDirectory")
    $deskShortcut = $shell.CreateShortcut((Join-Path $desktop "VibeDeck.lnk"))
    $deskShortcut.TargetPath = $hostExe
    $deskShortcut.Arguments = "--open"
    $deskShortcut.WorkingDirectory = $InstallDir
    $deskShortcut.WindowStyle = 7
    $deskShortcut.Description = "Open VibeDeck on this PC"
    if (Test-Path -LiteralPath $iconPath) {
        $deskShortcut.IconLocation = "$iconPath,0"
    }
    $deskShortcut.Save()
}

if (-not $SkipAutostart) {
    $runPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $runCommand = "`"$hostExe`""
    New-Item -Path $runPath -Force | Out-Null
    New-ItemProperty -Path $runPath -Name $runValueName -Value $runCommand -PropertyType String -Force | Out-Null
    Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $runValueName -ErrorAction SilentlyContinue
    Set-VibeDeckFirewallRule -ProgramPath $hostExe
} else {
    Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $runValueName -ErrorAction SilentlyContinue
    Remove-VibeDeckFirewallRule
}

Write-Host ""
Write-Host "VibeDeck installed." -ForegroundColor Green
Write-Host "  Web UI:  $webUrl"
Write-Host "  Files:   $InstallDir"
Write-Host "  Data:    $env:ProgramData\VibeDeck"
Write-Host "  Startup: $(if ($SkipAutostart) { 'Disabled' } else { 'Signed-in desktop session (Automatic)' })"
Write-Host ""
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $hostExe
$startInfo.Arguments = "--open"
$startInfo.WorkingDirectory = $InstallDir
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
[System.Diagnostics.Process]::Start($startInfo) | Out-Null
