Set-Location "C:\Production-pro-main\Production-pro-main\mobile\VidikomCrew"
$env:PATH = "C:\Program Files\nodejs;" + $env:PATH
$env:REACT_NATIVE_PACKAGER_HOSTNAME = "192.168.1.3"
while ($true) {
    $listening = Get-NetTCPConnection -LocalPort 8081 -State Listen -ErrorAction SilentlyContinue
    if (-not $listening) {
        & "C:\Program Files\nodejs\node.exe" "C:\Production-pro-main\Production-pro-main\mobile\VidikomCrew\node_modules\expo\bin\cli" start --go --tunnel --port 8081 *>> "C:\Production-pro-main\Production-pro-main\mobile\VidikomCrew\expo-tunnel.log"
    }
    Start-Sleep -Seconds 5
}
