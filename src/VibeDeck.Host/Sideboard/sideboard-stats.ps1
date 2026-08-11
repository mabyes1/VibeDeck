$ErrorActionPreference = "Stop"

function Round-Stat {
    param(
        [Parameter(Mandatory = $true)]
        [double]$Value,
        [int]$Digits = 1
    )

    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) {
        return $null
    }

    return [math]::Round($Value, $Digits)
}

function Parse-Double {
    param([object]$Value)

    $parsed = 0.0
    if ([double]::TryParse([string]$Value, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Try-Value {
    param(
        [scriptblock]$Script,
        $Fallback = $null
    )

    try {
        return & $Script
    } catch {
        return $Fallback
    }
}

function Get-WeatherCodeLabel {
    param([Nullable[int]]$Code)

    if ($null -eq $Code) {
        return $null
    }

    switch ($Code) {
        0 { return "Clear" }
        1 { return "Mostly clear" }
        2 { return "Partly cloudy" }
        3 { return "Cloudy" }
        45 { return "Fog" }
        48 { return "Rime fog" }
        { $_ -in 51, 53, 55 } { return "Drizzle" }
        { $_ -in 56, 57 } { return "Freezing drizzle" }
        { $_ -in 61, 63, 65 } { return "Rain" }
        { $_ -in 66, 67 } { return "Freezing rain" }
        { $_ -in 71, 73, 75 } { return "Snow" }
        77 { return "Snow grains" }
        { $_ -in 80, 81, 82 } { return "Rain showers" }
        { $_ -in 85, 86 } { return "Snow showers" }
        95 { return "Thunderstorm" }
        { $_ -in 96, 99 } { return "Thunderstorm with hail" }
        default { return "Weather" }
    }
}

function New-WeatherUnavailable {
    param([string]$ErrorMessage = $null)

    return [ordered]@{
        location = "Weather"
        temperatureC = $null
        apparentTemperatureC = $null
        weatherCode = $null
        description = $null
        isDay = $null
        updatedAt = $null
        source = $null
        error = $ErrorMessage
    }
}

function Get-WeatherSnapshot {
    $cacheDirectory = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "VibeDeck"
    $cachePath = Join-Path $cacheDirectory "sideboard-weather.json"
    $cacheVersion = 2
    $cacheTtl = [TimeSpan]::FromMinutes(10)
    $nowUtc = (Get-Date).ToUniversalTime()
    $cached = Try-Value {
        if (Test-Path -LiteralPath $cachePath) {
            Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
        }
    } $null

    if ($cached -and $cached.updatedAt -and $cached.cacheVersion -eq $cacheVersion) {
        $updatedAt = Try-Value { [datetime]$cached.updatedAt } $null
        if ($updatedAt -and ($nowUtc - $updatedAt.ToUniversalTime()) -lt $cacheTtl) {
            return $cached
        }
    }

    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

        $latitudeRaw = $env:VIBEDECK_WEATHER_LAT
        $longitudeRaw = $env:VIBEDECK_WEATHER_LON
        $location = $env:VIBEDECK_WEATHER_LOCATION
        $latitude = Parse-Double $latitudeRaw
        $longitude = Parse-Double $longitudeRaw
        $fallbackLocation = $null

        if ($latitude -eq $null -or $longitude -eq $null) {
            $geo = Invoke-RestMethod -Uri "https://ipwho.is/" -TimeoutSec 5
            if ($geo.success -eq $false) {
                throw "IP location lookup failed."
            }

            $latitude = Parse-Double $geo.latitude
            $longitude = Parse-Double $geo.longitude
            $city = [string]$geo.city
            $country = [string]$geo.country
            $fallbackLocation = (@($city, $country) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ", "
        }

        if ($latitude -eq $null -or $longitude -eq $null) {
            throw "Weather coordinates are unavailable."
        }

        $culture = [Globalization.CultureInfo]::InvariantCulture
        $latText = ([double]$latitude).ToString($culture)
        $lonText = ([double]$longitude).ToString($culture)

        if ([string]::IsNullOrWhiteSpace($location)) {
            $localizedLocation = Try-Value {
                $reverseUrl = "https://api-bdc.net/data/reverse-geocode-client?latitude=$latText&longitude=$lonText&localityLanguage=zh"
                $reverse = Invoke-RestMethod -Uri $reverseUrl -TimeoutSec 5
                $parts = @()
                if (-not [string]::IsNullOrWhiteSpace([string]$reverse.city)) {
                    $parts += [string]$reverse.city
                }
                if (-not [string]::IsNullOrWhiteSpace([string]$reverse.locality) -and
                    [string]$reverse.locality -ne [string]$reverse.city) {
                    $parts += [string]$reverse.locality
                }
                if (-not $parts.Count -and -not [string]::IsNullOrWhiteSpace([string]$reverse.principalSubdivision)) {
                    $parts += [string]$reverse.principalSubdivision
                }

                ($parts | Select-Object -Unique) -join " "
            } $null

            if (-not [string]::IsNullOrWhiteSpace($localizedLocation)) {
                $location = $localizedLocation
            } elseif (-not [string]::IsNullOrWhiteSpace($fallbackLocation)) {
                $location = $fallbackLocation
            } else {
                $location = "Current location"
            }
        }

        $weatherUrl = "https://api.open-meteo.com/v1/forecast?latitude=$latText&longitude=$lonText&current=temperature_2m,apparent_temperature,weather_code,is_day&timezone=auto"
        $weather = Invoke-RestMethod -Uri $weatherUrl -TimeoutSec 5
        $current = $weather.current
        $code = if ($current.weather_code -ne $null) { [int]$current.weather_code } else { $null }

        $snapshot = [ordered]@{
            cacheVersion = $cacheVersion
            location = $location
            temperatureC = Round-Stat (Parse-Double $current.temperature_2m)
            apparentTemperatureC = Round-Stat (Parse-Double $current.apparent_temperature)
            weatherCode = $code
            description = Get-WeatherCodeLabel $code
            isDay = if ($current.is_day -ne $null) { ([int]$current.is_day) -eq 1 } else { $null }
            updatedAt = $nowUtc.ToString("o")
            source = "open-meteo"
            error = $null
        }

        New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
        $snapshot | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath $cachePath -Encoding UTF8
        return $snapshot
    } catch {
        if ($cached) {
            return $cached
        }

        return New-WeatherUnavailable $_.Exception.Message
    }
}

function Get-HardwareSensorValue {
    param(
        [string[]]$TypePatterns,
        [string[]]$NamePatterns
    )

    $namespaces = @(
        "root/LibreHardwareMonitor",
        "root/OpenHardwareMonitor"
    )

    foreach ($namespace in $namespaces) {
        $sensors = Try-Value {
            Get-CimInstance -Namespace $namespace -ClassName Sensor -ErrorAction Stop
        } $null

        if ($null -eq $sensors) {
            continue
        }

        foreach ($sensor in $sensors) {
            $sensorType = [string]$sensor.SensorType
            $sensorName = [string]$sensor.Name

            $typeMatch = $false
            foreach ($pattern in $TypePatterns) {
                if ($sensorType -match $pattern) {
                    $typeMatch = $true
                    break
                }
            }

            if (-not $typeMatch) {
                continue
            }

            $nameMatch = $false
            foreach ($pattern in $NamePatterns) {
                if ($sensorName -match $pattern) {
                    $nameMatch = $true
                    break
                }
            }

            if (-not $nameMatch) {
                continue
            }

            $value = Parse-Double $sensor.Value
            if ($value -ne $null) {
                return $value
            }
        }
    }

    return $null
}

$now = Get-Date
$hostname = $env:COMPUTERNAME
$localIp = Try-Value {
    ([System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) |
        Where-Object {
            $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork -and
            -not $_.IPAddressToString.StartsWith("169.254.") -and
            $_.IPAddressToString -ne "127.0.0.1"
        } |
        Select-Object -First 1 -ExpandProperty IPAddressToString)
} "--"

$cpuUsage = Try-Value {
    Round-Stat ((Get-Counter "\Processor(_Total)\% Processor Time").CounterSamples[0].CookedValue)
} $null

$cpuTemperature = Try-Value {
    $fromMonitor = Get-HardwareSensorValue @("^Temperature$") @("CPU", "Package", "Tctl", "Tdie", "Core")
    if ($fromMonitor -ne $null) {
        return Round-Stat $fromMonitor
    }

    $thermal = Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop |
        Select-Object -First 1
    if ($null -eq $thermal) {
        return $null
    }

    Round-Stat (($thermal.CurrentTemperature / 10) - 273.15)
} $null

$osInfo = Get-CimInstance Win32_OperatingSystem
$totalMemoryBytes = [double]$osInfo.TotalVisibleMemorySize * 1KB
$freeMemoryBytes = [double]$osInfo.FreePhysicalMemory * 1KB
$usedMemoryBytes = $totalMemoryBytes - $freeMemoryBytes
$memoryPercent = if ($totalMemoryBytes -gt 0) {
    Round-Stat (($usedMemoryBytes / $totalMemoryBytes) * 100)
} else {
    $null
}

$diskInfo = Try-Value {
    Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
} $null

$diskUsedBytes = if ($diskInfo) {
    [double]$diskInfo.Size - [double]$diskInfo.FreeSpace
} else {
    $null
}

$diskUsagePercent = if ($diskInfo -and [double]$diskInfo.Size -gt 0) {
    Round-Stat (($diskUsedBytes / [double]$diskInfo.Size) * 100)
} else {
    $null
}

$diskCounters = Try-Value {
    Get-Counter "\PhysicalDisk(_Total)\Disk Read Bytes/sec", "\PhysicalDisk(_Total)\Disk Write Bytes/sec"
} $null

$diskReadMBps = $null
$diskWriteMBps = $null

if ($diskCounters) {
    foreach ($sample in $diskCounters.CounterSamples) {
        if ($sample.Path -like "*Disk Read Bytes/sec*") {
            $diskReadMBps = Round-Stat (([double]$sample.CookedValue) / 1MB)
        } elseif ($sample.Path -like "*Disk Write Bytes/sec*") {
            $diskWriteMBps = Round-Stat (([double]$sample.CookedValue) / 1MB)
        }
    }
}

$networkCounters = Try-Value {
    Get-Counter "\Network Interface(*)\Bytes Received/sec", "\Network Interface(*)\Bytes Sent/sec"
} $null

$downloadMbps = $null
$uploadMbps = $null

if ($networkCounters) {
    $rx = 0.0
    $tx = 0.0

    foreach ($sample in $networkCounters.CounterSamples) {
        $instanceName = [string]$sample.InstanceName
        if ($instanceName -match "loopback|isatap|teredo|tunnel") {
            continue
        }

        if ($sample.Path -like "*Bytes Received/sec*") {
            $rx += [double]$sample.CookedValue
        } elseif ($sample.Path -like "*Bytes Sent/sec*") {
            $tx += [double]$sample.CookedValue
        }
    }

    $downloadMbps = Round-Stat (($rx * 8) / 1000000)
    $uploadMbps = Round-Stat (($tx * 8) / 1000000)
}

$bootTime = [datetime]$osInfo.LastBootUpTime
$uptimeSeconds = [int](($now - $bootTime).TotalSeconds)

$gpuName = Try-Value {
    (Get-CimInstance Win32_VideoController |
        Where-Object { $_.Name } |
        Select-Object -First 1 -ExpandProperty Name)
} "Unknown GPU"

$gpuUsage = $null
$gpuTemperature = $null
$gpuMemoryUsedMb = $null
$gpuMemoryTotalMb = $null

$nvidiaSmi = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
if ($nvidiaSmi) {
    $gpuRow = Try-Value {
        & $nvidiaSmi.Source --query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total --format=csv,noheader,nounits 2>$null |
            Select-Object -First 1
    } $null

    if ($gpuRow) {
        $parts = ($gpuRow -split ",") | ForEach-Object { $_.Trim() }
        if ($parts.Count -ge 5) {
            $gpuName = $parts[0]
            $gpuUsage = Parse-Double $parts[1]
            $gpuTemperature = Parse-Double $parts[2]
            $gpuMemoryUsedMb = Parse-Double $parts[3]
            $gpuMemoryTotalMb = Parse-Double $parts[4]
        }
    }
}

if ($null -eq $gpuTemperature) {
    $gpuTemperature = Try-Value {
        $value = Get-HardwareSensorValue @("^Temperature$") @("GPU", "NVIDIA", "GeForce", "AMD", "Radeon")
        if ($value -ne $null) {
            return Round-Stat $value
        }

        return $null
    } $null
}

if ($null -eq $gpuUsage) {
    $engineCounters = Try-Value {
        Get-Counter "\GPU Engine(*)\Utilization Percentage"
    } $null

    if ($engineCounters) {
        $maxGpu = 0.0
        foreach ($sample in $engineCounters.CounterSamples) {
            $value = [double]$sample.CookedValue
            if ($value -gt $maxGpu) {
                $maxGpu = $value
            }
        }

        $gpuUsage = Round-Stat $maxGpu
    }
}

if ($null -eq $gpuMemoryTotalMb) {
    $gpuMemoryTotalMb = Try-Value {
        Round-Stat ((Get-CimInstance Win32_VideoController |
                Where-Object { $_.AdapterRAM } |
                Select-Object -First 1 -ExpandProperty AdapterRAM) / 1MB)
    } $null
}

$gpuMemoryPercent = if ($gpuMemoryUsedMb -ne $null -and $gpuMemoryTotalMb -gt 0) {
    Round-Stat (($gpuMemoryUsedMb / $gpuMemoryTotalMb) * 100)
} else {
    $null
}

$topProcesses = Try-Value {
    Get-Process |
        Where-Object {
            $_.ProcessName -notin @("Idle", "System", "Registry", "Memory Compression")
        } |
        Sort-Object WorkingSet64 -Descending |
        Select-Object -First 4 @(
            @{
                Name = "name"
                Expression = { $_.ProcessName }
            },
            @{
                Name = "memoryMb"
                Expression = { [math]::Round($_.WorkingSet64 / 1MB) }
            }
        )
} @()

$result = [ordered]@{
    generatedAt = $now.ToUniversalTime().ToString("o")
    system = [ordered]@{
        hostname = $hostname
        localIp = $localIp
        uptimeSeconds = $uptimeSeconds
        lastBoot = $bootTime.ToString("o")
    }
    cpu = [ordered]@{
        usagePercent = $cpuUsage
        temperatureC = $cpuTemperature
    }
    memory = [ordered]@{
        usedGb = Round-Stat ($usedMemoryBytes / 1GB)
        totalGb = Round-Stat ($totalMemoryBytes / 1GB)
        usagePercent = $memoryPercent
    }
    gpu = [ordered]@{
        name = $gpuName
        usagePercent = if ($gpuUsage -ne $null) { Round-Stat $gpuUsage } else { $null }
        temperatureC = if ($gpuTemperature -ne $null) { Round-Stat $gpuTemperature } else { $null }
        memoryUsedMb = if ($gpuMemoryUsedMb -ne $null) { [math]::Round($gpuMemoryUsedMb) } else { $null }
        memoryTotalMb = if ($gpuMemoryTotalMb -ne $null) { [math]::Round($gpuMemoryTotalMb) } else { $null }
        memoryUsagePercent = $gpuMemoryPercent
    }
    disk = [ordered]@{
        drive = "C:"
        usedGb = if ($diskUsedBytes -ne $null) { Round-Stat ($diskUsedBytes / 1GB) } else { $null }
        totalGb = if ($diskInfo) { Round-Stat (([double]$diskInfo.Size) / 1GB) } else { $null }
        usagePercent = $diskUsagePercent
        readMBps = $diskReadMBps
        writeMBps = $diskWriteMBps
    }
    network = [ordered]@{
        downMbps = $downloadMbps
        upMbps = $uploadMbps
    }
    weather = Get-WeatherSnapshot
    processes = $topProcesses
    sensors = [ordered]@{
        cpuTemperatureAvailable = ($cpuTemperature -ne $null)
        gpuTemperatureAvailable = ($gpuTemperature -ne $null)
    }
    error = $null
}

$result | ConvertTo-Json -Depth 6 -Compress
