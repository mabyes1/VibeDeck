#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadPath,
    [Parameter(Mandatory = $true)]
    [string]$LogPath
)

$ErrorActionPreference = "Stop"
try {
    & (Join-Path $PSScriptRoot "install-windows-product.ps1") `
        -PayloadPath $PayloadPath `
        -SkipDesktopIcon *> $LogPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    exit 0
}
catch {
    $_ | Format-List * -Force | Out-File -LiteralPath $LogPath -Append -Encoding utf8
    exit 1
}
