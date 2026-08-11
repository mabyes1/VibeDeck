[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$installRoot = "C:\Program Files\VibeDeck"
$hostExe = Join-Path $installRoot "VibeDeck.Host.exe"
$runPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "VibeDeckHost"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$serviceSids = @("S-1-5-18", "S-1-5-19", "S-1-5-20")

if ([Diagnostics.Process]::GetCurrentProcess().SessionId -le 0 -or $serviceSids -contains $identity.User.Value) {
    throw "VibeDeck autostart repair must run in the signed-in desktop session. Use the approved elevated broker action when invoking it from the MCP service."
}

if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw "Installed VibeDeck Host is missing: $hostExe"
}

$process = Start-Process -FilePath $hostExe -ArgumentList "--register-autostart" -WorkingDirectory $installRoot -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "VibeDeck Host autostart registration exited with code $($process.ExitCode)."
}

$actual = (Get-ItemProperty -Path $runPath -Name $runValueName -ErrorAction SilentlyContinue).VibeDeckHost
$expected = "`"$hostExe`""
if ($actual -ne $expected) {
    throw "VibeDeck Host did not register the expected signed-in-user autostart value."
}

Write-Host "VIBEDECK_AUTOSTART_REPAIRED"
