@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
set "MWB_EXIT_CODE=%ERRORLEVEL%"

echo.
if "%MWB_EXIT_CODE%"=="0" (
    echo Mouse Without Borders was installed successfully.
    echo Open it manually from the Start menu when you want to use it.
) else (
    echo Installation failed. Review the error shown above.
)

pause
exit /b %MWB_EXIT_CODE%
