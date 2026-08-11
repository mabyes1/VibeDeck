[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:5000",
    [switch]$KeepSource
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")
$sourceKey = "smoke-" + (Get-Date -Format "MMddHHmmss")
$actionHeader = "X-VibeDeck-Action-Token"
$token = $null

function Invoke-Json {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [ValidateSet("GET", "POST", "PATCH", "DELETE")][string]$Method = "GET",
        [hashtable]$Headers,
        [object]$Body,
        [int]$ExpectedStatus = 200
    )

    $requestHeaders = @{}
    if ($Headers) { $Headers.GetEnumerator() | ForEach-Object { $requestHeaders[$_.Key] = $_.Value } }
    $jsonBody = $null
    if ($null -ne $Body) {
        $jsonBody = $Body | ConvertTo-Json -Depth 8 -Compress
        $requestHeaders["Content-Type"] = "application/json"
    }

    try {
        $response = Invoke-WebRequest -Uri ($BaseUrl + $Path) -Method $Method -Headers $requestHeaders -Body $jsonBody -UseBasicParsing
        if ([int]$response.StatusCode -ne $ExpectedStatus) {
            throw "Expected HTTP $ExpectedStatus, got $($response.StatusCode) for $Method $Path"
        }
        if ([string]::IsNullOrWhiteSpace($response.Content)) { return $null }
        return $response.Content | ConvertFrom-Json
    }
    catch {
        $webResponse = $_.Exception.Response
        if ($webResponse -and [int]$webResponse.StatusCode -eq $ExpectedStatus) {
            $reader = New-Object System.IO.StreamReader($webResponse.GetResponseStream())
            try { return ($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose() }
        }
        throw
    }
}

try {
    $health = Invoke-Json "/health"
    if ($health.status -ne "ok") { throw "Health endpoint did not return status=ok" }

    $session = Invoke-Json "/api/session"
    if ([string]::IsNullOrWhiteSpace($session.actionToken)) { throw "Missing action token from /api/session" }
    $action = @{ $actionHeader = $session.actionToken }

    $created = Invoke-Json "/api/custom-sources" "POST" $action @{
        sourceKey = $sourceKey
        displayName = "Custom Sources Smoke Test"
        card = @{ type = "message-feed"; title = "Smoke messages"; maxItems = 5 }
    } 201
    $token = $created.ingest.token
    if ([string]::IsNullOrWhiteSpace($token)) { throw "Create response did not return the one-time source token" }

    $sourceHeaders = @{ Authorization = "Bearer $token" }
    Invoke-Json "/api/custom-sources/$sourceKey/events" "POST" $sourceHeaders @{
        id = "smoke-1"
        from = "verify-custom-sources.ps1"
        text = "VibeDeck custom source smoke test"
    } | Out-Null

    $cards = Invoke-Json "/api/custom-cards"
    $card = @($cards.cards | Where-Object { $_.sourceKey -eq $sourceKey }) | Select-Object -First 1
    if (-not $card) { throw "Created source was not present in /api/custom-cards" }
    if ($card.content.items[0].id -ne "smoke-1") { throw "Ingested message was not present in the card snapshot" }

    Invoke-Json "/api/custom-sources/$sourceKey/events" "POST" @{ Authorization = "Bearer vds_invalid" } @{ id = "bad"; text = "must fail" } 401 | Out-Null
    Invoke-Json "/api/custom-sources/$sourceKey/items/smoke-1" "DELETE" $sourceHeaders $null | Out-Null

    Write-Host "Custom Sources smoke test passed: $sourceKey"
}
finally {
    if ($token -and -not $KeepSource) {
        try { Invoke-Json "/api/custom-sources/$sourceKey" "DELETE" $action $null | Out-Null } catch { Write-Warning "Cleanup failed for $sourceKey`: $($_.Exception.Message)" }
    }
}
