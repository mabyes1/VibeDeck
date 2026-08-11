$ErrorActionPreference = "Stop"

Write-Host "This installs the Windows driver build toolchain through winget."
Write-Host "It can take a long time and may require administrator prompts."

winget install --id Microsoft.VisualStudio.2022.BuildTools --exact --source winget --location "D:\DevTools\VSBuildTools2022" --override "--installPath D:\DevTools\VSBuildTools2022 --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive --wait --norestart"
winget install --id Microsoft.VisualStudio.2022.Community --exact --source winget --location "D:\DevTools\VS2022Community" --override "--installPath D:\DevTools\VS2022Community --add Microsoft.VisualStudio.Workload.NativeDesktop --add Component.Microsoft.Windows.DriverKit --includeRecommended --passive --wait --norestart"
winget install --id Microsoft.WindowsWDK.10.0.26100 --exact --source winget

Write-Host "After installation, restart this terminal and run scripts\build-driver.ps1"
