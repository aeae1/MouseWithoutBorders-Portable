@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Check-Installation.ps1"
set "MWB_EXIT_CODE=%ERRORLEVEL%"

echo.
if "%MWB_EXIT_CODE%"=="0" (
    echo All Mouse Without Borders installation checks passed.
) else (
    echo One or more checks need attention. See the report saved on your Desktop.
)

pause
exit /b %MWB_EXIT_CODE%
