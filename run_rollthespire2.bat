@echo off
setlocal
cd /d "%~dp0"
if not exist "RollTheSpire2.exe" (
  echo RollTheSpire2.exe not found in this folder.
  echo This launcher is intended for the published portable package.
  pause
  exit /b 1
)
start "" "%~dp0RollTheSpire2.exe"
