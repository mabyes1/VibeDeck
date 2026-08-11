$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$setupHttps = Join-Path $PSScriptRoot "setup-https.ps1"
$localDotNet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotNet) { $localDotNet } else { "dotnet" }

& $setupHttps -Quiet

Write-Host "VibeDeck Host"
Write-Host "HTTP : http://0.0.0.0:5000"
Write-Host "HTTPS: https://0.0.0.0:5443"
Write-Host ""
Write-Host "First run: open http://127.0.0.1:5000 on this PC."
Write-Host "Then open one of these phone URLs in Safari or Chrome:"

try {
    $phoneAddresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
        Where-Object {
            $_.IPAddress -and
            $_.IPAddress -ne "127.0.0.1" -and
            -not $_.IPAddress.StartsWith("169.254.")
        } |
        Select-Object -ExpandProperty IPAddress -Unique)

    foreach ($address in $phoneAddresses) {
        Write-Host "  http://${address}:5000"
        Write-Host "  https://${address}:5443"
    }
}
catch {
    Write-Host "  Find the PC's IP with 'ipconfig', then open http://<PC-IP>:5000"
}

Write-Host "Use the PC page's QR code or open the URL manually. Approve the phone's pairing request on the PC."
Write-Host "Press Ctrl+C in this window to stop the Host."

$listener = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $activeHost = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    throw "Another VibeDeck Host already owns port 5000 (PID $($listener.OwningProcess), Session $($activeHost.SessionId)). Close the installed Host before starting this source build. The notification companion may remain open."
}

& $dotnet run `
    --no-launch-profile `
    --project (Join-Path $root "src\VibeDeck.Host")
