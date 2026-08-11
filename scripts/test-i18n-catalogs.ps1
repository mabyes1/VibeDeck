[CmdletBinding()]
param(
    [string]$LocaleRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($LocaleRoot)) {
    $LocaleRoot = Join-Path $PSScriptRoot "..\src\VibeDeck.Host\wwwroot\locales"
}

function Get-LeafKeys {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [string]$Prefix = ""
    )

    if ($Value -is [System.Collections.IDictionary] -or $Value -is [PSCustomObject]) {
        $names = if ($Value -is [System.Collections.IDictionary]) {
            $Value.Keys
        } else {
            $Value.PSObject.Properties.Name
        }

        foreach ($name in $names) {
            $next = if ($Prefix) { "$Prefix.$name" } else { [string]$name }
            $child = if ($Value -is [System.Collections.IDictionary]) { $Value[$name] } else { $Value.$name }
            Get-LeafKeys -Value $child -Prefix $next
        }
        return
    }

    $Prefix
}

$canonicalPath = Join-Path $LocaleRoot "zh-Hant.json"
if (-not (Test-Path -LiteralPath $canonicalPath)) {
    throw "Canonical catalog not found: $canonicalPath"
}

$canonical = Get-Content -LiteralPath $canonicalPath -Raw -Encoding UTF8 | ConvertFrom-Json
$canonicalKeys = @(Get-LeafKeys -Value $canonical | Sort-Object -Unique)

foreach ($locale in "en", "ja") {
    $path = Join-Path $LocaleRoot "$locale.json"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Catalog not found: $path"
    }

    $catalog = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $keys = @(Get-LeafKeys -Value $catalog | Sort-Object -Unique)
    $difference = @(Compare-Object -ReferenceObject $canonicalKeys -DifferenceObject $keys)
    if ($difference) {
        $missing = @($difference | Where-Object SideIndicator -eq '<=' | ForEach-Object InputObject)
        $extra = @($difference | Where-Object SideIndicator -eq '=>' | ForEach-Object InputObject)
        $details = @()
        if ($missing) { $details += "missing: $($missing -join ', ')" }
        if ($extra) { $details += "extra: $($extra -join ', ')" }
        throw "Catalog key parity failed for $locale ($($details -join '; '))."
    }
}

$catalogedSourceText = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function Add-CatalogText {
    param([object]$Value)

    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        [void]$catalogedSourceText.Add($Value)
        return
    }
    if ($Value -is [Collections.IDictionary]) {
        foreach ($entry in $Value.GetEnumerator()) { Add-CatalogText -Value $entry.Value }
        return
    }
    foreach ($property in $Value.PSObject.Properties) {
        Add-CatalogText -Value $property.Value
    }
}

Add-CatalogText -Value $canonical.ui
foreach ($property in $canonical.legacy.PSObject.Properties) {
    [void]$catalogedSourceText.Add($property.Name)
}

$webRoot = Split-Path -Parent $LocaleRoot
$missingRuntimeStrings = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$doubleQuotedLiteral = [regex]'"((?:\\.|[^"\\])*)"'
foreach ($script in Get-ChildItem -LiteralPath $webRoot -Recurse -File -Filter *.js) {
    $content = Get-Content -LiteralPath $script.FullName -Raw -Encoding UTF8
    foreach ($match in $doubleQuotedLiteral.Matches($content)) {
        if ($match.Value.Contains("`n") -or $match.Value.Contains("`r")) { continue }
        try { $value = $match.Value | ConvertFrom-Json } catch { continue }
        if ($value -isnot [string] -or $value -notmatch '[\u3400-\u9fff]') { continue }
        if (-not $catalogedSourceText.Contains($value)) {
            [void]$missingRuntimeStrings.Add($value)
        }
    }
}

if ($missingRuntimeStrings.Count -gt 0) {
    throw "Runtime localization coverage failed (missing exact source strings: $(@($missingRuntimeStrings | Sort-Object) -join ' | '))."
}

Write-Host "i18n catalog key parity passed."
