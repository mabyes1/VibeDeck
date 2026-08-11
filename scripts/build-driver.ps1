$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "driver\VibeDeck.Idd.sln"
$sampleDriver = Join-Path $root "third_party\Windows-driver-samples\video\IndirectDisplay\IddSampleDriver\Driver.cpp"

if (-not (Test-Path $sampleDriver)) {
    & (Join-Path $PSScriptRoot "fetch-idd-sample.ps1")
}

& (Join-Path $PSScriptRoot "patch-idd-sample.ps1")

$vsRoots = @(
    "D:\DevTools\VS2022Community",
    "D:\DevTools\VSBuildTools2022",
    "C:\Program Files\Microsoft Visual Studio",
    "C:\Program Files (x86)\Microsoft Visual Studio"
)

$msbuild = Get-ChildItem $vsRoots -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\MSBuild\Current\Bin\MSBuild.exe" } |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $msbuild) {
    throw "MSBuild was not found. Run scripts\install-driver-toolchain.ps1 first."
}

& $msbuild $solution /p:Configuration=Debug /p:Platform=x64 /p:SpectreMitigation=false /m
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
