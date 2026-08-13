Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Write-SetupStep([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function New-SetupContext {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [string]$Version
    )

    $project = Join-Path $RepoRoot "src\VibeDeck.Host\VibeDeck.Host.csproj"
    $packagingRoot = Join-Path $RepoRoot "packaging\windows-setup"
    $artifactRoot = Join-Path $RepoRoot "artifacts\windows-setup"
    $projectXml = [xml][IO.File]::ReadAllText($project, [Text.Encoding]::UTF8)
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = [string]$projectXml.Project.PropertyGroup.Version
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must look like 0.1.0 (got '$Version')"
    }

    [pscustomobject]@{
        RepoRoot = $RepoRoot
        Configuration = $Configuration
        Version = $Version
        Project = $project
        PackagingRoot = $packagingRoot
        ArtifactRoot = $artifactRoot
        PayloadRoot = Join-Path $artifactRoot "payload"
        FastPayloadRoot = Join-Path $artifactRoot "fast-local-payload"
        DependencyRoot = Join-Path $artifactRoot "dependencies"
        InstalledRoot = Join-Path $env:ProgramFiles "VibeDeck"
        IssPath = Join-Path $packagingRoot "VibeDeck.iss"
        IconPath = Join-Path $packagingRoot "vibedeck.ico"
        ProductCheckScript = Join-Path $RepoRoot "scripts\test-product-flow.ps1"
        SignScript = Join-Path $RepoRoot "scripts\sign-release.ps1"
    }
}

function Assert-SetupInputs {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [switch]$FastLocal,
        [switch]$WebOnly,
        [switch]$SkipInno
    )
    foreach ($input in @($Context.Project, $Context.IssPath, $Context.IconPath)) {
        if (-not (Test-Path -LiteralPath $input -PathType Leaf)) {
            throw "Required Setup input is missing: $input"
        }
    }
    if ($WebOnly -and -not $FastLocal) {
        throw "WebOnly can only be used together with FastLocal."
    }
    if ($FastLocal -and $SkipInno) {
        throw "FastLocal produces an Inno overlay Setup and cannot be combined with SkipInno."
    }
}

function Remove-RepoDirectory {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $resolved = [IO.Path]::GetFullPath($Path)
    $repoPrefix = [IO.Path]::GetFullPath($Context.RepoRoot).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the repository: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Invoke-SetupSourceChecks {
    param([Parameter(Mandatory = $true)]$Context)
    Write-SetupStep "Running product source checks"
    & $Context.ProductCheckScript -Source
    if ($LASTEXITCODE -ne 0) { throw "Product source checks failed." }
}

function Get-VerifiedDependency {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256,
        [Parameter(Mandatory = $true)][string]$Description
    )
    if (Test-Path -LiteralPath $Path) {
        if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $ExpectedSha256) {
            return $Path
        }
        Remove-Item -LiteralPath $Path -Force
    }
    Write-SetupStep "Downloading verified $Description"
    New-Item -ItemType Directory -Path $Context.DependencyRoot -Force | Out-Null
    $download = "$Path.download"
    try {
        Invoke-WebRequest -Uri $Url -OutFile $download -UseBasicParsing
        $actual = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
        if ($actual -ne $ExpectedSha256) {
            throw "$Description checksum mismatch. Expected $ExpectedSha256, got $actual."
        }
        Move-Item -LiteralPath $download -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $download) {
            Remove-Item -LiteralPath $download -Force
        }
    }
    return $Path
}

function Publish-SetupPayload {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [switch]$ReuseRuntimeCache
    )
    Write-SetupStep "Publishing VibeDeck Host (self-contained win-x64)"
    if ($ReuseRuntimeCache -and (Test-Path -LiteralPath $Context.PayloadRoot)) {
        foreach ($relative in @("wwwroot", "Installers")) {
            $replaceable = Join-Path $Context.PayloadRoot $relative
            if (Test-Path -LiteralPath $replaceable) {
                Remove-RepoDirectory -Context $Context -Path $replaceable
            }
        }
    } else {
        Remove-RepoDirectory -Context $Context -Path $Context.PayloadRoot
    }
    New-Item -ItemType Directory -Path $Context.PayloadRoot -Force | Out-Null

    dotnet publish $Context.Project `
        -c $Context.Configuration `
        -r win-x64 `
        --self-contained true `
        -p:Version=$($Context.Version) `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $Context.PayloadRoot
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    Copy-Item -LiteralPath (Join-Path $Context.PackagingRoot "product-install.json") -Destination $Context.PayloadRoot -Force
    Copy-Item -LiteralPath $Context.IconPath -Destination $Context.PayloadRoot -Force

    $cloudflaredVersion = "2026.7.2"
    $cloudflared = Get-VerifiedDependency `
        -Context $Context `
        -Url "https://github.com/cloudflare/cloudflared/releases/download/$cloudflaredVersion/cloudflared-windows-amd64.exe" `
        -Path (Join-Path $Context.DependencyRoot "cloudflared-$cloudflaredVersion-windows-amd64.exe") `
        -ExpectedSha256 "CDB5D4432F6AE1595654A692A51308B69D2BF7AF961F5578D9391837CF072DF9" `
        -Description "Cloudflare connector $cloudflaredVersion"
    $cloudflaredLicense = Get-VerifiedDependency `
        -Context $Context `
        -Url "https://raw.githubusercontent.com/cloudflare/cloudflared/$cloudflaredVersion/LICENSE" `
        -Path (Join-Path $Context.DependencyRoot "cloudflared-$cloudflaredVersion-LICENSE.txt") `
        -ExpectedSha256 "58D1E17FFE5109A7AE296CAAFCADFDBE6A7D176F0BC4AB01E12A689B0499D8BD" `
        -Description "cloudflared license $cloudflaredVersion"

    $signature = Get-AuthenticodeSignature -FilePath $cloudflared
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $signature.SignerCertificate.Subject -notmatch 'O="?Cloudflare, Inc\."?') {
        throw "cloudflared Authenticode signature is not a valid Cloudflare, Inc. signature."
    }

    $connectorRoot = Join-Path $Context.PayloadRoot "connectors"
    $licenseRoot = Join-Path $Context.PayloadRoot "licenses"
    New-Item -ItemType Directory -Path $connectorRoot, $licenseRoot -Force | Out-Null
    Copy-Item -LiteralPath $cloudflared -Destination (Join-Path $connectorRoot "cloudflared.exe") -Force
    Copy-Item -LiteralPath $cloudflaredLicense -Destination (Join-Path $licenseRoot "cloudflared-LICENSE.txt") -Force

    foreach ($relative in @("VibeDeck.Host.exe", "wwwroot", "Installers\install-virtual-display.ps1")) {
        if (-not (Test-Path -LiteralPath (Join-Path $Context.PayloadRoot $relative))) {
            throw "Publish output is missing $relative"
        }
    }
    & $Context.ProductCheckScript -Payload -PayloadPath $Context.PayloadRoot
    if ($LASTEXITCODE -ne 0) { throw "Published payload checks failed." }
    Write-Host "Payload ready: $($Context.PayloadRoot)"
}

function Assert-ExistingSetupInstall {
    param([Parameter(Mandatory = $true)]$Context)
    if (-not (Test-Path -LiteralPath (Join-Path $Context.InstalledRoot "product-install.json"))) {
        throw "FastLocal requires an existing canonical Setup installation at '$($Context.InstalledRoot)'. Run the full Setup once first."
    }
}

function Test-SameFile([string]$Left, [string]$Right) {
    if (-not (Test-Path -LiteralPath $Left) -or -not (Test-Path -LiteralPath $Right)) {
        return $false
    }
    if ((Get-Item -LiteralPath $Left).Length -ne (Get-Item -LiteralPath $Right).Length) {
        return $false
    }
    return (Get-FileHash -LiteralPath $Left -Algorithm SHA256).Hash -eq
        (Get-FileHash -LiteralPath $Right -Algorithm SHA256).Hash
}

function Copy-RelativeFile([string]$SourceRoot, [string]$DestinationRoot, [string]$RelativePath) {
    $destination = Join-Path $DestinationRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $SourceRoot $RelativePath) -Destination $destination -Force
}

function Write-PayloadSummary([string]$Path, [string]$Label) {
    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File)
    if ($files.Count -eq 0) { throw "$Label contains no files." }
    $sizeMb = [Math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
    Write-Host "$Label`: $($files.Count) files / $sizeMb MB" -ForegroundColor Green
}

function New-ChangedFilePayload {
    param([Parameter(Mandatory = $true)]$Context)
    Assert-ExistingSetupInstall -Context $Context
    Write-SetupStep "Building changed-file overlay for the installed Host"
    Remove-RepoDirectory -Context $Context -Path $Context.FastPayloadRoot
    New-Item -ItemType Directory -Path $Context.FastPayloadRoot -Force | Out-Null

    $payloadPrefix = [IO.Path]::GetFullPath($Context.PayloadRoot).TrimEnd('\') + '\'
    foreach ($source in (Get-ChildItem -LiteralPath $Context.PayloadRoot -Recurse -File)) {
        $relative = $source.FullName.Substring($payloadPrefix.Length)
        $replaceable = $relative.StartsWith("wwwroot\", [StringComparison]::OrdinalIgnoreCase) -or
            $relative.StartsWith("Installers\", [StringComparison]::OrdinalIgnoreCase)
        if ($replaceable -or -not (Test-SameFile $source.FullName (Join-Path $Context.InstalledRoot $relative))) {
            Copy-RelativeFile $Context.PayloadRoot $Context.FastPayloadRoot $relative
        }
    }
    Write-PayloadSummary -Path $Context.FastPayloadRoot -Label "Fast local payload"
    Write-Host "Unchanged .NET runtime, cloudflared, and notification companion files are not included."
}

function New-WebOnlyPayload {
    param([Parameter(Mandatory = $true)]$Context)
    Assert-ExistingSetupInstall -Context $Context
    $sourceWebRoot = Join-Path $Context.RepoRoot "src\VibeDeck.Host\wwwroot"
    foreach ($required in @("index.html", "index.js")) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceWebRoot $required))) {
            throw "Source wwwroot is missing $required."
        }
    }
    Write-SetupStep "Building web-only overlay for the installed Host"
    Remove-RepoDirectory -Context $Context -Path $Context.FastPayloadRoot
    $destinationWebRoot = Join-Path $Context.FastPayloadRoot "wwwroot"
    New-Item -ItemType Directory -Path $destinationWebRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $sourceWebRoot -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destinationWebRoot -Recurse -Force
    }
    Write-PayloadSummary -Path $Context.FastPayloadRoot -Label "Web-only fast payload"
    Write-Host "dotnet publish, runtime files, cloudflared, and notification companion are skipped."
}

function Find-InnoCompiler {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 7\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path -LiteralPath $path)) { return $path }
    }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

function Get-InnoCompiler {
    param([switch]$InstallIfMissing)
    $compiler = Find-InnoCompiler
    if ($compiler) { return $compiler }
    if (-not $InstallIfMissing) { return $null }
    Write-SetupStep "Installing Inno Setup 6 via winget"
    & winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements | Out-Host
    $compiler = Find-InnoCompiler
    if (-not $compiler) { throw "Inno Setup installed but ISCC.exe still was not found." }
    return $compiler
}

function Invoke-InnoSetupBuild {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$Compiler,
        [switch]$FastLocal
    )
    Write-SetupStep "Compiling Setup with Inno Setup"
    New-Item -ItemType Directory -Path $Context.ArtifactRoot -Force | Out-Null
    $payload = if ($FastLocal) { $Context.FastPayloadRoot } else { $Context.PayloadRoot }
    $arguments = @(
        "/DMyAppVersion=$($Context.Version)",
        "/DMyPayloadDir=$payload",
        "/DMyOutputDir=$($Context.ArtifactRoot)"
    )
    if ($FastLocal) { $arguments += "/DFastLocal=1" }
    $arguments += $Context.IssPath
    & $Compiler @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

    $name = if ($FastLocal) {
        "VibeDeck-FastLocal-Setup-$($Context.Version).exe"
    } else {
        "VibeDeck-Setup-$($Context.Version).exe"
    }
    $setup = Get-Item -LiteralPath (Join-Path $Context.ArtifactRoot $name) -ErrorAction SilentlyContinue
    if (-not $setup) { throw "Setup.exe was not produced: $name" }
    return $setup
}

function Write-ReleaseIntegrityArtifacts {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)]$Setup,
        [string]$SigningKeyPath
    )
    Write-SetupStep "Writing update checksum"
    $hash = (Get-FileHash -LiteralPath $Setup.FullName -Algorithm SHA256).Hash
    [IO.File]::WriteAllText("$($Setup.FullName).sha256", "$hash  $($Setup.Name)`n")
    Write-Host "  $($Setup.FullName).sha256"
    if ($SigningKeyPath) {
        Write-SetupStep "Signing release (detached update-integrity signature)"
        & $Context.SignScript -Sign -KeyPath $SigningKeyPath -InstallerPath $Setup.FullName
    } else {
        Write-Host ""
        Write-Host "NOTE: no SigningKeyPath was supplied; Setup has no detached release signature." -ForegroundColor Yellow
    }
}

Export-ModuleMember -Function @(
    "Assert-SetupInputs",
    "Get-InnoCompiler",
    "Invoke-InnoSetupBuild",
    "Invoke-SetupSourceChecks",
    "New-ChangedFilePayload",
    "New-SetupContext",
    "New-WebOnlyPayload",
    "Publish-SetupPayload",
    "Write-ReleaseIntegrityArtifacts"
)
