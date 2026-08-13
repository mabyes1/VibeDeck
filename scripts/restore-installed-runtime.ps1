#Requires -Version 5.1
<#
.SYNOPSIS
  Compatibility shortcut for repairing an installed Host from current source.

.DESCRIPTION
  Runtime repair now goes through FastLocal Setup instead of copying three
  managed files directly and leaving the installation in a partial state.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build-and-install-windows.ps1") -FastLocal -SkipTests
exit $LASTEXITCODE
