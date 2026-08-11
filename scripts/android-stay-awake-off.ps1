$ErrorActionPreference = "Stop"

$adb = Get-Command adb -ErrorAction SilentlyContinue
if (-not $adb) {
    $scrcpyAdb = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages\Genymobile.scrcpy_Microsoft.Winget.Source_8wekyb3d8bbwe\scrcpy-win64-v3.3.4\adb.exe"
    if (Test-Path $scrcpyAdb) {
        $adb = Get-Item $scrcpyAdb
    }
}

if (-not $adb) {
    throw "adb was not found. Install Android platform-tools or scrcpy first."
}

& $adb.Source shell settings put global stay_on_while_plugged_in 0
$value = & $adb.Source shell settings get global stay_on_while_plugged_in
Write-Host "stay_on_while_plugged_in=$value"
Write-Host "Phone screen timeout is back to the Android system setting."
