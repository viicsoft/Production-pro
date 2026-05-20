@echo off
REM Unregister ATEM SDK COM DLL
echo Unregistering ATEM SDK...

REM Get the installation directory (passed as parameter)
set "INSTALL_DIR=%~1"

REM Unregister the 64-bit COM DLL
"%SystemRoot%\System32\regsvr32.exe" /u /s "%INSTALL_DIR%desktop\BMDSwitcherAPI64.dll"

if %ERRORLEVEL% EQU 0 (
    echo ATEM SDK unregistered successfully.
) else (
    echo Warning: Failed to unregister ATEM SDK. Error code: %ERRORLEVEL%
)

exit /b 0
