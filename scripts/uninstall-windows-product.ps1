#Requires -Version 5.1
<#
.SYNOPSIS
  Remove a VibeDeck product install created by install-windows-product.ps1 or Setup.
#>
[CmdletBinding()]
param(
    [string]$InstallDir,
    [switch]$KeepData
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    throw "Run this script in an elevated PowerShell (Administrator)."
}

if (-not $InstallDir) {
    $InstallDir = Join-Path ${env:ProgramFiles} "VibeDeck"
}

$serviceName = "VibeDeckHost"
$runValueName = "VibeDeckHost"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq "Running") {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
    & sc.exe delete $serviceName | Out-Null
    Write-Host "Removed service $serviceName"
}

Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $runValueName -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $runValueName -ErrorAction SilentlyContinue
foreach ($processName in @("VibeDeck.Host.exe")) {
    Get-CimInstance Win32_Process -Filter "Name='$processName'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

Get-NetFirewallRule -DisplayName "VibeDeck Host" -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction SilentlyContinue

$startMenu = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\VibeDeck"
if (Test-Path -LiteralPath $startMenu) {
    Remove-Item -LiteralPath $startMenu -Recurse -Force
}

$desktopLnk = Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) "VibeDeck.lnk"
if (Test-Path -LiteralPath $desktopLnk) {
    Remove-Item -LiteralPath $desktopLnk -Force
}

if (Test-Path -LiteralPath $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir"
}

if (-not $KeepData) {
    $data = Join-Path $env:ProgramData "VibeDeck"
    if (Test-Path -LiteralPath $data) {
        Remove-Item -LiteralPath $data -Recurse -Force
        Write-Host "Removed $data"
    }
}

Write-Host "VibeDeck uninstall complete."
