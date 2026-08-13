#Requires -Version 5.1
<#
.SYNOPSIS
  Compatibility shortcut for a web-only local Setup update.

.DESCRIPTION
  Kept for existing developer muscle memory and elevated-broker actions. The
  implementation now uses the same-AppId FastLocal Setup path; it no longer
  copies files directly into Program Files.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build-and-install-windows.ps1") `
    -FastLocal `
    -WebOnly `
    -SkipTests
exit $LASTEXITCODE
