#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$FastLocal,
    [switch]$WebOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\VibeDeck.Host\VibeDeck.Host.csproj"
$packageScript = Join-Path $PSScriptRoot "package-windows-setup.ps1"

Write-Host $(if ($FastLocal) { "VibeDeck fast local Setup update" } else { "VibeDeck one-click install / update" }) -ForegroundColor Cyan
& $packageScript -InstallInno -SkipTests:$SkipTests -FastLocal:$FastLocal -WebOnly:$WebOnly
if ($LASTEXITCODE -ne 0) { throw "VibeDeck Setup build failed." }

$projectXml = [xml][IO.File]::ReadAllText($project, [Text.Encoding]::UTF8)
$version = [string]$projectXml.Project.PropertyGroup.Version
$setupFileName = if ($FastLocal) { "VibeDeck-FastLocal-Setup-$version.exe" } else { "VibeDeck-Setup-$version.exe" }
$setup = Join-Path $repoRoot "artifacts\windows-setup\$setupFileName"
if (-not (Test-Path -LiteralPath $setup)) {
    throw "Setup was not produced: $setup"
}

Write-Host $(if ($FastLocal) { "Starting same-AppId fast local Setup update..." } else { "Starting canonical Setup update..." }) -ForegroundColor Cyan
$oldListener = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
$oldHostPid = if ($oldListener) { [int]$oldListener.OwningProcess } else { 0 }
$setupArguments = '/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /TASKS="desktopicon,autostart"'
$process = Start-Process -FilePath $setup -ArgumentList $setupArguments -PassThru
# Start-Process -Wait can wait for the long-running Host that Setup launches.
# WaitForExit tracks only Setup itself, then the loop below verifies the new Host.
$process.WaitForExit()
$setupExitCode = $process.ExitCode
if ($setupExitCode -ne 0) {
    throw "VibeDeck Setup exited with code $setupExitCode."
}

$deadline = [DateTime]::UtcNow.AddSeconds(45)
do {
    Start-Sleep -Milliseconds 400
    $newListener = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
} while ((-not $newListener -or ($oldHostPid -gt 0 -and [int]$newListener.OwningProcess -eq $oldHostPid)) -and
    [DateTime]::UtcNow -lt $deadline)
if (-not $newListener) { throw "Updated Host did not start on port 5000." }
if ($oldHostPid -gt 0 -and [int]$newListener.OwningProcess -eq $oldHostPid) {
    throw "Setup returned but the old Host process was not replaced."
}

& (Join-Path $PSScriptRoot "test-product-flow.ps1") -Installed
if ($LASTEXITCODE -ne 0) { throw "Installed-product verification failed." }
Write-Host "VibeDeck install / update completed." -ForegroundColor Green
