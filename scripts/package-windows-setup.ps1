#Requires -Version 5.1
<#
.SYNOPSIS
  Build the canonical VibeDeck Setup or a local-only overlay Setup.

.DESCRIPTION
  This file contains orchestration only. Dependency, publish, payload-diff, and
  Inno helpers live in windows-setup\SetupPackaging.psm1.

  Normal:    clean self-contained publish -> release Setup
  FastLocal: incremental publish -> changed-file overlay Setup
  WebOnly:   source wwwroot -> web-only overlay Setup (no dotnet publish)
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [string]$Version,
    [switch]$SkipInno,
    [switch]$InstallInno,
    [switch]$SkipTests,
    [switch]$FastLocal,
    [switch]$WebOnly,
    [string]$SigningKeyPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot "windows-setup\SetupPackaging.psm1") -Force

$context = New-SetupContext `
    -RepoRoot $repoRoot `
    -Configuration $Configuration `
    -Version $Version
Assert-SetupInputs `
    -Context $context `
    -FastLocal:$FastLocal `
    -WebOnly:$WebOnly `
    -SkipInno:$SkipInno

if (-not $SkipTests) {
    Invoke-SetupSourceChecks -Context $context
}

if ($WebOnly) {
    New-WebOnlyPayload -Context $context
} else {
    Publish-SetupPayload -Context $context -ReuseRuntimeCache:$FastLocal
    if ($FastLocal) {
        New-ChangedFilePayload -Context $context
    }
}

if ($SkipInno) {
    Write-Host "Payload published without compiling Setup: $($context.PayloadRoot)"
    exit 0
}

$compiler = Get-InnoCompiler -InstallIfMissing:$InstallInno
if (-not $compiler) {
    Write-Warning "Inno Setup compiler was not found; payload is ready at $($context.PayloadRoot)."
    Write-Host "Install it with: winget install JRSoftware.InnoSetup"
    exit 0
}

$setup = Invoke-InnoSetupBuild `
    -Context $context `
    -Compiler $compiler `
    -FastLocal:$FastLocal

if ($FastLocal) {
    Write-Host ""
    Write-Host "Fast local overlay Setup ready:" -ForegroundColor Green
    Write-Host "  $($setup.FullName)"
    Write-Host "Local existing installation only - never publish this artifact."
    exit 0
}

Write-ReleaseIntegrityArtifacts `
    -Context $context `
    -Setup $setup `
    -SigningKeyPath $SigningKeyPath

Write-Host ""
Write-Host "VibeDeck release Setup ready:" -ForegroundColor Green
Write-Host "  $($setup.FullName)"
