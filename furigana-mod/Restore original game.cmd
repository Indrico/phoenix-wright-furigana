@echo off
rem Close the game before restoring. The backup is the unmodified original DLL.
if not exist "%~dp0backups\Assembly-CSharp.dll" (
    echo Original backup is missing. Nothing changed.
    pause
    exit /b 1
)
copy /Y "%~dp0backups\Assembly-CSharp.dll" "%~dp0..\PWAAT_Data\Managed\Assembly-CSharp.dll"
if errorlevel 1 (
    echo Restore failed. Close the game and try again.
    pause
    exit /b 1
)
echo Original game restored. Furigana mod disabled.
pause
