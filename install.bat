@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-and-install-windows.ps1"
if errorlevel 1 (
  echo.
  echo VibeDeck installation failed. See the error above.
  pause
  exit /b 1
)
echo.
echo VibeDeck is installed and running.
pause
