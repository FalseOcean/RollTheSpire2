@echo off
setlocal
cd /d "%~dp0"
echo Building RollTheSpire2...
dotnet build RollWpf\RollWpf.csproj -c Release
if errorlevel 1 goto fail

echo.
echo Build succeeded.
echo Run with: run_wpf.bat
echo Executable: RollWpf\bin\Release\net9.0-windows\RollTheSpire2.exe
exit /b 0

:fail
echo.
echo ERROR: build failed.
pause
exit /b 1
