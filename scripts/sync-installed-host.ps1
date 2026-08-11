[CmdletBinding()]
param(
    [string]$PayloadPath = (Join-Path $PSScriptRoot "..\artifacts\dev-host-charset-fix")
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$administratorRole = [Security.Principal.WindowsBuiltInRole]::Administrator
if (-not $principal.IsInRole($administratorRole)) {
    $absolutePayloadPath = [IO.Path]::GetFullPath($PayloadPath)
    $process = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-PayloadPath", "`"$absolutePayloadPath`""
    )
    exit $process.ExitCode
}

$installRoot = "C:\Program Files\VibeDeck"
$resolvedInstallRoot = (Resolve-Path -LiteralPath $installRoot).Path
$resolvedPayloadPath = (Resolve-Path -LiteralPath $PayloadPath).Path
if ($resolvedInstallRoot -ne $installRoot) {
    throw "Refusing to update an unexpected install path: $resolvedInstallRoot"
}

if (-not (Test-Path -LiteralPath (Join-Path $resolvedPayloadPath "VibeDeck.Host.exe"))) {
    throw "Published Host payload is missing VibeDeck.Host.exe: $resolvedPayloadPath"
}

Get-CimInstance Win32_Process -Filter "Name='VibeDeck.Host.exe'" |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

Start-Sleep -Milliseconds 500
Get-ChildItem -LiteralPath $resolvedPayloadPath -Force |
    Copy-Item -Destination $installRoot -Recurse -Force

Start-Process -FilePath (Join-Path $installRoot "VibeDeck.Host.exe") -WorkingDirectory $installRoot -WindowStyle Hidden
Write-Host "Synced and restarted the installed Host without running Setup."
