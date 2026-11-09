@echo off
rem Removes the two Classic Mobile services. Asks for administrator rights if needed.
net session >nul 2>&1
if errorlevel 1 (
  echo Asking for administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-Services.ps1"
echo.
pause
