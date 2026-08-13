#Requires -Version 5.1
<#
.SYNOPSIS
  Compatibility shortcut for a changed-file local Setup update.

.DESCRIPTION
  The old script copied an ad-hoc payload directly into Program Files. It now
  delegates to the supported same-AppId FastLocal Setup workflow.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build-and-install-windows.ps1") -FastLocal -SkipTests
exit $LASTEXITCODE
