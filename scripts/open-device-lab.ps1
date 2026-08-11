$ErrorActionPreference = "Stop"

$healthUrl = "http://127.0.0.1:5000/health"
$labUrl = "http://localhost:5000/device-lab.html"

try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 3
    if ($response.StatusCode -ne 200) {
        throw "Host returned HTTP $($response.StatusCode)."
    }
}
catch {
    throw "VibeDeck Host is not running. Start the installed Host or source development Host first."
}

Start-Process $labUrl
