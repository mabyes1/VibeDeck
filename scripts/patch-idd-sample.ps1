$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$driver = Join-Path $root "third_party\Windows-driver-samples\video\IndirectDisplay\IddSampleDriver\Driver.cpp"

if (-not (Test-Path $driver)) {
    throw "Microsoft IDD sample Driver.cpp not found. Run scripts\fetch-idd-sample.ps1 first."
}

$text = Get-Content $driver -Raw
$text = $text -replace 'static constexpr DWORD IDD_SAMPLE_MONITOR_COUNT = 3;.*', 'static constexpr DWORD IDD_SAMPLE_MONITOR_COUNT = 1; // VibeDeck exposes one virtual display for now.'
Set-Content -Path $driver -Value $text -NoNewline

$header = Join-Path $root "third_party\Windows-driver-samples\video\IndirectDisplay\IddSampleDriver\Driver.h"
$headerText = Get-Content $header -Raw
$headerText = $headerText -replace 'static constexpr size_t szModeList = 3;', 'static constexpr size_t szModeList = 12;'
$headerText = $headerText -replace 'static constexpr size_t szModeList = 9;', 'static constexpr size_t szModeList = 12;'
Set-Content -Path $header -Value $headerText -NoNewline

Write-Host "Patched Microsoft IDD sample to expose one virtual monitor."
