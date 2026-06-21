@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set APP_NAME=RollTheSpire2
set APP_VERSION=v2.1.0
set RUNTIME=win-x64
set OUT_DIR=publish\%APP_NAME%_%APP_VERSION%_%RUNTIME%

echo Publishing %APP_NAME% %APP_VERSION% for %RUNTIME%...
if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"

dotnet publish RollWpf\RollWpf.csproj -c Release -r %RUNTIME% --self-contained true -o "%OUT_DIR%"
if errorlevel 1 goto fail

echo Copying runtime data and documentation...
robocopy data "%OUT_DIR%\data" /E >nul
if errorlevel 8 goto fail

if not exist "%OUT_DIR%\profiles" mkdir "%OUT_DIR%\profiles"
if exist profiles\.keep copy /Y profiles\.keep "%OUT_DIR%\profiles\.keep" >nul
if exist profiles\README.md copy /Y profiles\README.md "%OUT_DIR%\profiles\README.md" >nul

copy /Y config.json "%OUT_DIR%\config.json" >nul
copy /Y README.md "%OUT_DIR%\README.md" >nul
if exist README_EN.md copy /Y README_EN.md "%OUT_DIR%\README_EN.md" >nul
if exist CHANGELOG.md copy /Y CHANGELOG.md "%OUT_DIR%\CHANGELOG.md" >nul
copy /Y run_rollthespire2.bat "%OUT_DIR%\run_rollthespire2.bat" >nul

echo.
echo Publish succeeded:
echo %OUT_DIR%
echo.
echo End users should run:
echo %OUT_DIR%\RollTheSpire2.exe
exit /b 0

:fail
echo.
echo ERROR: publish failed.
pause
exit /b 1
