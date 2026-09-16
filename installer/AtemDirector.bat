@echo off
REM AtemDirector Launcher
setlocal
set "INSTALL_DIR=%~dp0"
cd /d "%INSTALL_DIR%"

start "" "%INSTALL_DIR%desktop\desktop.exe"
exit /b 0
