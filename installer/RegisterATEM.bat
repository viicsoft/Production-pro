@echo off
REM Register ATEM SDK COM DLL
echo Registering ATEM SDK...

REM Get the installation directory (passed as parameter)
set "INSTALL_DIR=%~1"

REM Register the 64-bit COM DLL
"%SystemRoot%\System32\regsvr32.exe" /s "%INSTALL_DIR%desktop\BMDSwitcherAPI64.dll"

if %ERRORLEVEL% EQU 0 (
    echo ATEM SDK registered successfully.
) else (
    echo Warning: Failed to register ATEM SDK. Error code: %ERRORLEVEL%
)

exit /b 0
