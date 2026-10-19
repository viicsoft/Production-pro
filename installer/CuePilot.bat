@echo off
REM CuePilot Launcher
setlocal
set "INSTALL_DIR=%~dp0"
cd /d "%INSTALL_DIR%"

start "" "%INSTALL_DIR%desktop\Vidikom.exe"
exit /b 0
