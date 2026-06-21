@echo off
setlocal
cd /d "%~dp0"
set EXE=RollWpf\bin\Release\net9.0-windows\RollTheSpire2.exe
if not exist "%EXE%" (
  echo RollTheSpire2.exe not found. Please run build_windows_wpf.bat first.
  pause
  exit /b 1
)
start "" "%EXE%"
