$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$thirdParty = Join-Path $root "third_party"
$sampleRepo = Join-Path $thirdParty "Windows-driver-samples"

if (-not (Test-Path $thirdParty)) {
    New-Item -ItemType Directory $thirdParty | Out-Null
}

if (-not (Test-Path (Join-Path $sampleRepo ".git"))) {
    git clone --filter=blob:none --sparse https://github.com/microsoft/Windows-driver-samples.git $sampleRepo
}

git -C $sampleRepo sparse-checkout set video/IndirectDisplay
git -C $sampleRepo pull --ff-only

& (Join-Path $PSScriptRoot "patch-idd-sample.ps1")

Write-Host "Fetched Microsoft Indirect Display sample into $sampleRepo"
