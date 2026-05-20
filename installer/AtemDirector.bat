@echo off
REM AtemDirector Launcher
REM Detects IP, updates Caddyfile, and starts all services

setlocal enabledelayedexpansion

echo ========================================
echo   AtemDirector Launcher
echo ========================================
echo.

REM Get installation directory
set "INSTALL_DIR=%~dp0"
cd /d "%INSTALL_DIR%"

REM Detect IP addresses
echo [1/4] Detecting network interfaces...
set "IP_RESULT_FILE=%TEMP%\atem_ip_result.txt"
if exist "%IP_RESULT_FILE%" del "%IP_RESULT_FILE%"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ips = @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object {$_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.InterfaceAlias -notlike '*Hyper-V*' -and $_.InterfaceAlias -notlike '*vEthernet*' -and $_.InterfaceAlias -notlike '*WSL*' -and $_.InterfaceAlias -notlike '*Loopback*'} | Sort-Object {if ($_.InterfaceAlias -like '*Wi-Fi*' -or $_.InterfaceAlias -like '*Wireless*') {0} elseif ($_.InterfaceAlias -like '*Ethernet*') {1} else {2}} | Select-Object -ExpandProperty IPAddress); if ($ips.Count -eq 0) { exit 1 } if ($ips.Count -eq 1) { $ips[0] | Out-File -FilePath '%IP_RESULT_FILE%' -Encoding ascii } else { Write-Host 'Available IP Addresses:' -ForegroundColor Cyan; for ($i = 0; $i -lt $ips.Count; $i++) { Write-Host \"  [$($i+1)] $($ips[$i])\" }; $choice = Read-Host \"`nSelect IP (1-$($ips.Count))\"; $ips[[int]$choice - 1] | Out-File -FilePath '%IP_RESULT_FILE%' -Encoding ascii }"

if not exist "%IP_RESULT_FILE%" (
    echo.
    echo ERROR: IP detection cancelled or no network found.
    pause
    exit /b 1
)

set /p ATEM_IP=<"%IP_RESULT_FILE%"
del "%IP_RESULT_FILE%"

if "%ATEM_IP%"=="" (
    echo.
    echo ERROR: Could not determine IP address. 
    set /p ATEM_IP="Please enter your IP address manually: "
)

echo.
echo Using IP: %ATEM_IP%
echo.

REM Update Caddyfile
echo [2/4] Updating Caddyfile...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$c = Get-Content 'caddy\Caddyfile.template' -Raw; $c = $c -replace '\{\$ATEM_IP\}', '%ATEM_IP%'; $c | Set-Content 'caddy\Caddyfile'"

REM Start Caddy
echo [3/4] Starting Caddy HTTPS Proxy...
if not exist "caddy\Caddyfile" (
    echo ERROR: Caddyfile was not created!
    pause
    exit /b 1
)
start "AtemDirector-Caddy" /D "caddy" "caddy.exe" run --config "Caddyfile"
timeout /t 3 /nobreak >nul

REM Start Server
echo [4/4] Starting AtemDirector Server...
start "AtemDirector-Server" /D "server" "server.exe"
timeout /t 3 /nobreak >nul

REM Start Desktop
echo.
echo Starting Desktop Application...
start "" "desktop\desktop.exe"

echo.
echo ========================================
echo   AtemDirector Started!
echo ========================================
echo.
echo Access at: https://%ATEM_IP%:8443
echo.
echo IMPORTANT: If the browser says 'Connection Timed Out' or 'Not Private':
echo 1. Check if the laptop and phone are on the SAME WiFi / Hotspot.
echo 2. Click 'Advanced' or 'Show Details' on your phone and then 'Proceed'.
echo.
echo Keep the other windows open while using the app.
echo Press any key to stop all services and exit...
pause >nul

REM Cleanup
taskkill /F /IM caddy.exe /T >nul 2>&1
taskkill /F /IM server.exe /T >nul 2>&1
echo Services stopped.
timeout /t 2 >nul
