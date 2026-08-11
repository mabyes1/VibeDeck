[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResultPath
)

$ErrorActionPreference = "Stop"
$driverUrl = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip"
$driverSha256 = "e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a"
$nefconUrl = "https://github.com/nefarius/nefcon/releases/download/v1.14.0/nefcon_v1.14.0.zip"
$nefconSha256 = "a15557da24a9efca203158de3b43b0eaf982db231f0194031f1ed428bc13e669"
$workRoot = Join-Path $env:TEMP ("VibeDeck-VDD-" + [Guid]::NewGuid().ToString("N"))

function Write-InstallResult([bool]$success, [string]$code, [string]$detail = "", [bool]$restartRequired = $false) {
    $directory = Split-Path -Parent $ResultPath
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    @{
        Success = $success
        RestartRequired = $restartRequired
        Code = $code
        Detail = $detail
    } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
}

function Assert-FileHash([string]$path, [string]$expected) {
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "hash_mismatch"
    }
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "administrator_required"
    }

    New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
    $driverZip = Join-Path $workRoot "driver.zip"
    $nefconZip = Join-Path $workRoot "nefcon.zip"

    Invoke-WebRequest -Uri $driverUrl -OutFile $driverZip -UseBasicParsing
    Invoke-WebRequest -Uri $nefconUrl -OutFile $nefconZip -UseBasicParsing
    Assert-FileHash $driverZip $driverSha256
    Assert-FileHash $nefconZip $nefconSha256

    $driverRoot = Join-Path $workRoot "driver"
    $nefconRoot = Join-Path $workRoot "nefcon"
    Expand-Archive -LiteralPath $driverZip -DestinationPath $driverRoot -Force
    Expand-Archive -LiteralPath $nefconZip -DestinationPath $nefconRoot -Force

    $inf = Get-ChildItem -LiteralPath $driverRoot -Filter "MttVDD.inf" -Recurse -File | Select-Object -First 1
    $catalog = Get-ChildItem -LiteralPath $driverRoot -Filter "MttVDD.cat" -Recurse -File | Select-Object -First 1
    $settings = Get-ChildItem -LiteralPath $driverRoot -Filter "vdd_settings.xml" -Recurse -File | Select-Object -First 1
    $nefcon = Get-ChildItem -LiteralPath $nefconRoot -Filter "nefconc.exe" -Recurse -File |
        Where-Object { $_.FullName -match "[\\/]x64[\\/]" } |
        Select-Object -First 1

    if (-not $inf -or -not $catalog -or -not $settings -or -not $nefcon) {
        throw "package_incomplete"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $catalog.FullName
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "signature_invalid"
    }

    $certificates = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2Collection
    $certificates.Import([System.IO.File]::ReadAllBytes($catalog.FullName))
    foreach ($certificate in $certificates) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPublisher", "LocalMachine")
        try {
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $store.Add($certificate)
        }
        finally {
            $store.Close()
        }
    }

    $settingsRoot = "C:\VirtualDisplayDriver"
    New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
    $settingsPath = Join-Path $settingsRoot "vdd_settings.xml"
    Copy-Item -LiteralPath $settings.FullName -Destination $settingsPath -Force

    # Keep the production VDD useful for phones and tablets instead of leaving
    # Windows at the driver's 800x600 bootstrap mode. Runtime auto-selection
    # chooses among these modes based on the connected client's viewport.
    [xml]$settingsXml = Get-Content -LiteralPath $settingsPath -Raw
    # VibeDeck streams at <=60 fps. Keeping the upstream 90/120/144/165/244
    # global rates multiplies every custom resolution into a very large mode
    # table, which can make the virtual target fail to enumerate after reload.
    # Keep each resolution's 30 Hz fallback and advertise one shared 60 Hz mode.
    $globalNode = $settingsXml.SelectSingleNode("/vdd_settings/global")
    while ($globalNode.HasChildNodes) {
        [void]$globalNode.RemoveChild($globalNode.FirstChild)
    }
    $globalRefresh = $settingsXml.CreateElement("g_refresh_rate")
    $globalRefresh.InnerText = "60"
    [void]$globalNode.AppendChild($globalRefresh)

    $vibeDeckModes = @(
        @(812,375), @(974,450), @(1082,500), @(1218,562),
        @(1170,540), @(1404,648), @(1560,720),
        @(854,480), @(960,540), @(1024,576), @(1152,648), @(1280,720),
        @(1280,800), @(1440,900), @(1600,1000), @(1920,1200),
        @(1024,768), @(1280,960), @(1440,1080),
        @(1200,800), @(1440,960), @(1536,1024),
        @(1366,768), @(1600,900), @(1920,1080),
        @(1440,2560), @(1080,1920),
        @(800,1280), @(900,1440), @(1000,1600),
        @(768,1024), @(960,1280),
        @(800,1200), @(960,1440), @(1024,1536)
    )
    $existingModes = @{}
    foreach ($resolution in @($settingsXml.vdd_settings.resolutions.resolution)) {
        $existingModes["$($resolution.width)x$($resolution.height)"] = $true
    }
    $resolutionsNode = $settingsXml.SelectSingleNode("/vdd_settings/resolutions")
    foreach ($mode in $vibeDeckModes) {
        $key = "$($mode[0])x$($mode[1])"
        if ($existingModes.ContainsKey($key)) { continue }
        $resolution = $settingsXml.CreateElement("resolution")
        foreach ($field in @(@("width", $mode[0]), @("height", $mode[1]), @("refresh_rate", 30))) {
            $node = $settingsXml.CreateElement($field[0])
            $node.InnerText = [string]$field[1]
            [void]$resolution.AppendChild($node)
        }
        [void]$resolutionsNode.AppendChild($resolution)
        $existingModes[$key] = $true
    }
    $settingsXml.Save($settingsPath)

    $existingDevices = @(Get-PnpDevice -Class Display -ErrorAction SilentlyContinue |
        Where-Object { $_.FriendlyName -eq "Virtual Display Driver" })

    if ($existingDevices.Count -eq 0) {
        $logPath = Join-Path (Split-Path -Parent $ResultPath) "virtual-display-install.log"
        & $nefcon.FullName install $inf.FullName "Root\MttVDD" "--default-log-file=$logPath" --verbose
        if ($LASTEXITCODE -ne 0) {
            throw "driver_install_failed:$LASTEXITCODE"
        }
        $existingDevices = @(Get-PnpDevice -Class Display -ErrorAction SilentlyContinue |
            Where-Object { $_.FriendlyName -eq "Virtual Display Driver" })
    }

    if ($existingDevices.Count -gt 1) {
        foreach ($duplicate in ($existingDevices | Select-Object -Skip 1)) {
            pnputil /remove-device $duplicate.InstanceId | Out-Null
        }
    }

    $primaryDevice = $existingDevices | Select-Object -First 1
    if ($primaryDevice) {
        pnputil /restart-device $primaryDevice.InstanceId | Out-Null
    }
    pnputil /scan-devices | Out-Null
    Start-Sleep -Seconds 3
    Write-InstallResult $true "installed"
}
catch {
    $parts = $_.Exception.Message.Split(':', 2)
    $code = $parts[0]
    $detail = if ($parts.Count -gt 1) { $parts[1] } else { "" }
    if ($code -notin @("administrator_required", "hash_mismatch", "package_incomplete", "signature_invalid", "driver_install_failed")) {
        $code = "unexpected"
        $detail = ""
    }
    Write-InstallResult $false $code $detail
    exit 1
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
