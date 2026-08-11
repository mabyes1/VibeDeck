$ErrorActionPreference = "Stop"

$result = [ordered]@{
    Winget = $false
    Git = $false
    MSBuild = $false
    CppCompiler = $false
    WdkIddCxHeader = $false
    WdkWdfLibrary = $false
}

$result.Winget = [bool](Get-Command winget -ErrorAction SilentlyContinue)
$result.Git = [bool](Get-Command git -ErrorAction SilentlyContinue)

$vsRoots = @(
    "D:\DevTools\VS2022Community",
    "D:\DevTools\VSBuildTools2022",
    "C:\Program Files\Microsoft Visual Studio",
    "C:\Program Files (x86)\Microsoft Visual Studio"
)

$msbuild = Get-ChildItem $vsRoots -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\MSBuild\Current\Bin\MSBuild.exe" } |
    Select-Object -First 1
$result.MSBuild = [bool]$msbuild

$compiler = Get-ChildItem $vsRoots -Recurse -Filter cl.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\VC\Tools\MSVC\*\bin\Hostx64\x64\cl.exe" } |
    Select-Object -First 1
$result.CppCompiler = [bool]$compiler

$iddcx = Get-ChildItem "C:\Program Files (x86)\Windows Kits" -Recurse -Filter iddcx.h -ErrorAction SilentlyContinue |
    Select-Object -First 1
$result.WdkIddCxHeader = [bool]$iddcx

$wdf = Get-ChildItem "C:\Program Files (x86)\Windows Kits" -Recurse -Filter WdfDriverEntry.lib -ErrorAction SilentlyContinue |
    Select-Object -First 1
$result.WdkWdfLibrary = [bool]$wdf

$result.GetEnumerator() | ForEach-Object {
    $state = if ($_.Value) { "OK" } else { "MISSING" }
    "{0,-18} {1}" -f $_.Key, $state
}

if (-not ($result.MSBuild -and $result.CppCompiler -and $result.WdkIddCxHeader -and $result.WdkWdfLibrary)) {
    exit 1
}
