@echo off
rem Installs API 1 and API 2 as Windows services. Asks for administrator rights if needed.
net session >nul 2>&1
if errorlevel 1 (
  echo Asking for administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Services.ps1"
echo.
pause
