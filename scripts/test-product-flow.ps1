#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Source,
    [switch]$Payload,
    [switch]$Installed,
    [switch]$Responsive,
    [string]$PayloadPath,
    [switch]$RequireVirtualDisplay
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "VibeDeck.sln"
$project = Join-Path $repoRoot "src\VibeDeck.Host\VibeDeck.Host.csproj"

if (-not ($Source -or $Payload -or $Installed)) {
    $Source = $true
}

function Assert-Product([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Write-Check([string]$message) {
    Write-Host "[product-check] $message" -ForegroundColor Cyan
}

function Test-AclSidPresent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Sid
    )
    $acl = Get-Acl -LiteralPath $Path
    foreach ($rule in $acl.Access) {
        if ($null -eq $rule.IdentityReference) { continue }
        try {
            $ruleSid = $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
        }
        catch {
            continue
        }
        if ($ruleSid -eq $Sid) { return $true }
    }
    return $false
}

function Assert-VibeDeckDataAcl {
    param(
        [Parameter(Mandatory = $true)][string]$DataRoot
    )
    Assert-Product (Test-Path -LiteralPath $DataRoot) "ProgramData root missing: $DataRoot"
    foreach ($sid in @("S-1-5-32-545", "S-1-1-0", "S-1-5-11")) {
        Assert-Product (-not (Test-AclSidPresent -Path $DataRoot -Sid $sid)) `
            "ProgramData ACL still grants well-known SID $sid on $DataRoot. Users/Everyone/Authenticated Users must be removed."
    }
    Assert-Product (Test-AclSidPresent -Path $DataRoot -Sid "S-1-5-18") "ProgramData ACL missing SYSTEM (S-1-5-18) on $DataRoot"
    Assert-Product (Test-AclSidPresent -Path $DataRoot -Sid "S-1-5-32-544") "ProgramData ACL missing Administrators (S-1-5-32-544) on $DataRoot"

    $interactive = Test-AclSidPresent -Path $DataRoot -Sid "S-1-5-4"
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $currentUser = Test-AclSidPresent -Path $DataRoot -Sid $currentSid
    Assert-Product ($interactive -or $currentUser) `
        "ProgramData ACL must grant modify to the signed-in user or INTERACTIVE (S-1-5-4). CurrentSid=$currentSid"

    # Write probe as the current identity (desktop Host identity in -Installed runs).
    $probe = Join-Path $DataRoot (".acl-probe-" + [Guid]::NewGuid().ToString("N"))
    try {
        [IO.File]::WriteAllText($probe, "probe")
        Assert-Product (Test-Path -LiteralPath $probe) "Write probe failed to create $probe"
    }
    finally {
        if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue }
    }
}

function Get-SignedInUserRunValue([string]$name) {
    $userName = (Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).UserName
    if ([string]::IsNullOrWhiteSpace($userName)) { return $null }
    try {
        $account = [System.Security.Principal.NTAccount]::new($userName)
        $sid = $account.Translate([System.Security.Principal.SecurityIdentifier]).Value
        $item = Get-ItemProperty "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Run" -Name $name -ErrorAction SilentlyContinue
        if ($null -eq $item) { return $null }
        return $item.PSObject.Properties[$name].Value
    }
    catch {
        return $null
    }
}

if ($Source) {
    Write-Check "Release tests"
    $hostAssets = Join-Path $repoRoot "src\VibeDeck.Host\obj\project.assets.json"
    if (Test-Path -LiteralPath $hostAssets) {
        # Reuse an existing restore graph. Some machines have a broken NuGet
        # ConfigurationDefaults path that makes even `dotnet restore` fail;
        # CI and clean checkouts still restore on first run.
        & dotnet test $solution -c Release --no-restore
    }
    else {
        & dotnet test $solution -c Release
    }
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }

    Write-Check "Browser JavaScript syntax"
    $node = Get-Command node -ErrorAction Stop
    Get-ChildItem (Join-Path $repoRoot "src\VibeDeck.Host\wwwroot") -Recurse -Filter "*.js" -File |
        ForEach-Object {
            & $node.Source --check $_.FullName
            if ($LASTEXITCODE -ne 0) { throw "JavaScript syntax check failed: $($_.FullName)" }
        }

    Write-Check "Browser unit and responsive-layout contract tests"
    $browserTestFiles = @(
        Get-ChildItem @(
            (Join-Path $repoRoot "tests\js"),
            (Join-Path $repoRoot "tests\wwwroot")
        ) -Filter "*.test.mjs" -File -ErrorAction Stop |
            Sort-Object FullName |
            ForEach-Object FullName
    )
    Assert-Product ($browserTestFiles.Count -gt 0) "No browser contract tests were found."
    & $node.Source --test @browserTestFiles
    if ($LASTEXITCODE -ne 0) { throw "Browser unit or responsive-layout contract tests failed." }

    Write-Check "Managed connector Worker tests"
    $workerRoot = Join-Path $repoRoot "workers\vibedeck-connect-code"
    Push-Location $workerRoot
    try {
        & $node.Source --test
        if ($LASTEXITCODE -ne 0) { throw "Managed connector Worker tests failed." }
    }
    finally {
        Pop-Location
    }

    Write-Check "Product PowerShell syntax"
    foreach ($relative in @(
        "scripts\install-windows-product.ps1",
        "scripts\build-and-install-windows.ps1",
        "scripts\uninstall-windows-product.ps1",
        "scripts\package-windows-setup.ps1",
        "scripts\windows-setup\SetupPackaging.psm1",
        "scripts\package-windows-notifications.ps1",
        "scripts\repair-installed-autostart.ps1",
        "scripts\test-product-flow.ps1",
        "src\VibeDeck.Host\Installers\install-virtual-display.ps1"
    )) {
        $path = Join-Path $repoRoot $relative
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors) { throw "PowerShell syntax check failed: $relative`n$($errors -join "`n")" }
    }

    Write-Check "Canonical product path"
    Assert-Product (-not (Test-Path (Join-Path $repoRoot "apps\android"))) "Deprecated apps/android still exists. Phone clients must remain browser/PWA-only."
    Assert-Product (-not (Test-Path (Join-Path $repoRoot "scripts\package-release.ps1"))) "Deprecated portable ZIP release script still exists. Setup is the only product release path."
    Assert-Product (Test-Path (Join-Path $repoRoot "install.bat")) "One-click install entry is missing."
    Assert-Product (Test-Path (Join-Path $repoRoot "update.bat")) "One-click update entry is missing."
    Assert-Product (-not (Get-ChildItem (Join-Path $repoRoot "src\VibeDeck.Host\wwwroot") -Recurse -File -Include "*.apk","*.ipa" -ErrorAction SilentlyContinue)) "Native mobile package found under wwwroot."
    $webSource = Get-ChildItem (Join-Path $repoRoot "src\VibeDeck.Host\wwwroot") -Recurse -File -Include "*.js","*.css","*.html" |
        ForEach-Object { Get-Content $_.FullName -Raw }
    Assert-Product (($webSource -join "`n") -notmatch "native-shell") "Deprecated native-shell branch still exists in the browser/PWA client."
    $projectText = Get-Content $project -Raw
    Assert-Product ($projectText -notmatch "WindowsServices|Logging\.EventLog") "Host still references Windows Service packages."
    $readme = Get-Content (Join-Path $repoRoot "README.md") -Raw
    Assert-Product ($readme -notmatch "apps/android|package-release\.ps1") "README still points to a deprecated product path."
}

if ($Responsive) {
    Assert-Product $Source "-Responsive requires the source checks. Do not combine it with only -Payload or -Installed."
    Write-Check "Continuous responsive browser matrix"
    $listener = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    Assert-Product ($null -ne $listener) "The source Host is not listening on port 5000. Start it with start.bat before using -Responsive."
    $hostProcess = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
    $hostPath = [IO.Path]::GetFullPath($hostProcess.Path)
    Assert-Product ($hostPath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) "Port 5000 is not owned by the source Host: $hostPath"
    $npm = Get-Command npm.cmd -ErrorAction Stop
    Push-Location $repoRoot
    try {
        & $npm.Source run test:responsive
        if ($LASTEXITCODE -ne 0) { throw "Responsive browser matrix failed." }
    }
    finally {
        Pop-Location
    }
}

if ($Payload) {
    if (-not $PayloadPath) { $PayloadPath = Join-Path $repoRoot "artifacts\windows-setup\payload" }
    $PayloadPath = [IO.Path]::GetFullPath($PayloadPath)
    Write-Check "Published payload at $PayloadPath"
    foreach ($relative in @(
        "VibeDeck.Host.exe",
        "connectors\cloudflared.exe",
        "licenses\cloudflared-LICENSE.txt",
        "product-install.json",
        "vibedeck.ico",
        "wwwroot\index.html",
        "wwwroot\index.js",
        "Installers\install-virtual-display.ps1"
    )) {
        Assert-Product (Test-Path -LiteralPath (Join-Path $PayloadPath $relative)) "Payload is missing $relative."
    }
    foreach ($legacyLauncher in @("Open-VibeDeck.cmd", "Open-VibeDeck.vbs", "Start-VibeDeck-Host.vbs")) {
        Assert-Product (-not (Test-Path -LiteralPath (Join-Path $PayloadPath $legacyLauncher))) "Payload still contains legacy launcher: $legacyLauncher"
    }
    $connectorSignature = Get-AuthenticodeSignature -FilePath (Join-Path $PayloadPath "connectors\cloudflared.exe")
    Assert-Product ($connectorSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) "Payload cloudflared signature is invalid."
    Assert-Product ($connectorSignature.SignerCertificate.Subject -match 'O="?Cloudflare, Inc\."?') "Payload cloudflared signer is not Cloudflare, Inc."
    $projectText = Get-Content $project -Raw
    Assert-Product ($projectText -match "<OutputType>WinExe</OutputType>") "Host must be a native Windows background application."
    Assert-Product (-not (Get-ChildItem (Join-Path $PayloadPath "wwwroot") -Recurse -File -Include "*.apk","*.ipa" -ErrorAction SilentlyContinue)) "Payload contains a native mobile package."

    Write-Check "Fallback installer must not grant Users:Modify"
    $installScript = Get-Content (Join-Path $repoRoot "scripts\install-windows-product.ps1") -Raw
    Assert-Product ($installScript -notmatch "S-1-5-32-545:\(OI\)\(CI\)M") `
        "install-windows-product.ps1 still grants BUILTIN\Users modify on ProgramData."
    Assert-Product ($installScript -match "Get-VibeDeckDataUserSid") `
        "install-windows-product.ps1 must resolve the original signed-in user SID when run as SYSTEM."
    Assert-Product ($installScript -match "Invoke-IcaclsChecked") `
        "install-windows-product.ps1 must check every icacls exit code."
}

if ($Installed) {
    Write-Check "Live installed product"
    Assert-Product (-not (Get-Service "VibeDeckHost" -ErrorAction SilentlyContinue)) "Legacy VibeDeckHost Windows Service is still registered."
    $run = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "VibeDeckHost" -ErrorAction SilentlyContinue).VibeDeckHost
    if ([string]::IsNullOrWhiteSpace($run)) {
        $run = Get-SignedInUserRunValue "VibeDeckHost"
    }
    if ([string]::IsNullOrWhiteSpace($run)) {
        $run = (Get-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "VibeDeckHost" -ErrorAction SilentlyContinue).VibeDeckHost
    }
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $serviceIdentity = $currentSid -in @("S-1-5-18", "S-1-5-19", "S-1-5-20")
    if ([string]::IsNullOrWhiteSpace($run) -and $serviceIdentity) {
        Write-Warning "VibeDeckHost sign-in auto-start cannot be verified from the current service identity. Run this check in the signed-in desktop session for strict autostart validation."
    }
    else {
        Assert-Product (-not [string]::IsNullOrWhiteSpace($run)) "VibeDeckHost sign-in auto-start is missing. Repair it with scripts\repair-installed-autostart.ps1 from the signed-in desktop session (or the approved MCP elevated broker action)."
    }

    $listener = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    Assert-Product ($null -ne $listener) "Installed Host is not listening on port 5000."
    $hostProcess = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
    Assert-Product ($hostProcess.SessionId -gt 0) "Installed Host is running in Session 0; virtual display capture will fail."

    $displays = Invoke-RestMethod "http://127.0.0.1:5000/api/displays" -TimeoutSec 8
    Assert-Product (-not ($displays | Where-Object DeviceName -eq "WinDisc")) "Host is enumerating the Session 0 WinDisc display."
    if ($RequireVirtualDisplay) {
        Assert-Product ($null -ne ($displays | Where-Object IsVibeDeckDisplay | Select-Object -First 1)) "VibeDeck virtual display was not found."
    }

    Write-Check "ProgramData ACL hardening"
    $installedDataRoot = Join-Path $env:ProgramData "VibeDeck"
    Assert-VibeDeckDataAcl -DataRoot $installedDataRoot
}

Write-Host "Product flow checks passed." -ForegroundColor Green
