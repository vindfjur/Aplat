@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\build-installer.ps1" %*
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" echo Installer build failed. Read the error above.
pause
exit /b %RESULT%
