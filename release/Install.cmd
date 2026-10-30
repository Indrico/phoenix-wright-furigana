@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Patch.ps1" -Mode Install
if errorlevel 1 (
    echo Installation failed. Read the message above.
    pause
    exit /b 1
)
pause
