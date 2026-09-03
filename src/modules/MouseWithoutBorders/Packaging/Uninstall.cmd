@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
set "MWB_EXIT_CODE=%ERRORLEVEL%"

echo.
if "%MWB_EXIT_CODE%"=="0" (
    echo Mouse Without Borders was uninstalled successfully.
) else (
    echo Uninstallation failed. Review the error shown above.
)

pause
exit /b %MWB_EXIT_CODE%
