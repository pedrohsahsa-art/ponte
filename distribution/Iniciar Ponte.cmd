@echo off
setlocal
set "PONTE_DATA_DIR=%~dp0data"
start "Ponte" /D "%~dp0" "%~dp0app\Ponte.Windows.exe"
endlocal
