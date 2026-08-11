[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$administratorRole = [Security.Principal.WindowsBuiltInRole]::Administrator
if (-not $principal.IsInRole($administratorRole)) {
    # Windows services run in a non-interactive session.  RunAs cannot display
    # UAC there, so route this one fixed deployment action through the signed-in
    # user's WebGPT elevated broker instead of attempting a blind elevation.
    if ($identity.User.Value -eq "S-1-5-19") {
        $brokerClient = "C:\ProgramData\WebGPTCodingToolsMCPService\request-elevated-action.ps1"
        if (-not (Test-Path -LiteralPath $brokerClient -PathType Leaf)) {
            throw "elevation_required: interactive WebGPT elevated broker is not installed."
        }
        $windowsPowerShell = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
        & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $brokerClient -Action sync-installed-webroot
        exit $LASTEXITCODE
    }
    $pwshCommand = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    $elevatedShell = if ($pwshCommand) { $pwshCommand.Source } else { $null }
    if (-not $elevatedShell) {
        $elevatedShell = (Get-Command powershell.exe -ErrorAction Stop).Source
    }
    $process = Start-Process $elevatedShell -Verb RunAs -Wait -PassThru -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`""
    )
    exit $process.ExitCode
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot "src\VibeDeck.Host\wwwroot"
$installRoot = "C:\Program Files\VibeDeck\wwwroot"

if (-not (Test-Path -LiteralPath $sourceRoot)) {
    throw "Source web root is missing: $sourceRoot"
}

if (-not (Test-Path -LiteralPath $installRoot)) {
    throw "Installed VibeDeck web root is missing: $installRoot. Install Setup once before using this development helper."
}

$resolvedInstallRoot = (Resolve-Path -LiteralPath $installRoot).Path
if ($resolvedInstallRoot -ne "C:\Program Files\VibeDeck\wwwroot") {
    throw "Refusing to update an unexpected install path: $resolvedInstallRoot"
}

Get-ChildItem -LiteralPath $sourceRoot -Force |
    Copy-Item -Destination $installRoot -Recurse -Force

$cacheBust = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$indexPath = Join-Path $installRoot "index.html"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$index = [IO.File]::ReadAllText($indexPath, $utf8NoBom)
$index = $index -replace '(?<=/index\.css\?v=)\d+', $cacheBust
$index = $index -replace '(?<=/index\.js\?v=)\d+', $cacheBust
[IO.File]::WriteAllText($indexPath, $index, $utf8NoBom)

Write-Host "Synced source web files to the installed Host."
Write-Host "No Setup or Host restart was needed. Refresh the browser/PWA to load cache version $cacheBust."
