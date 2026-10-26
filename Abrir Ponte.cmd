@echo off
cd /d "%~dp0"
if not exist "dist\windows\Ponte.Windows.exe" (
  echo Compile primeiro com scripts\build-windows.ps1.
  pause
  exit /b 1
)
start "" "dist\windows\Ponte.Windows.exe"
