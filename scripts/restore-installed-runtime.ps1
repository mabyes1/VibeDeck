[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$administratorRole = [Security.Principal.WindowsBuiltInRole]::Administrator
if (-not $principal.IsInRole($administratorRole)) {
    $process = Start-Process (Join-Path $PSHOME "pwsh.exe") -Verb RunAs -Wait -PassThru -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`""
    )
    exit $process.ExitCode
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $repoRoot "artifacts\windows-setup\payload"
$install = "C:\Program Files\VibeDeck"

foreach ($name in @("VibeDeck.Host.runtimeconfig.json", "VibeDeck.Host.deps.json", "VibeDeck.Host.dll")) {
    Copy-Item -LiteralPath (Join-Path $payload $name) -Destination (Join-Path $install $name) -Force
}

Start-Process -FilePath (Join-Path $install "VibeDeck.Host.exe") -WorkingDirectory $install -WindowStyle Hidden
