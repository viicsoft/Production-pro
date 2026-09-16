# Challenger Empirical Stress Test: 5 Consecutive Runs of Verify-UI.ps1
$ErrorActionPreference = "Stop"

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " CHALLENGER EMPIRICAL STRESS TEST: 5 CONSECUTIVE RUNS" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

for ($i = 1; $i -le 5; $i++) {
    Write-Host "`n>>> [RUN $i/5] Executing scripts\Verify-UI.ps1..." -ForegroundColor Yellow
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    
    $proc = Start-Process -FilePath "powershell.exe" -ArgumentList "-ExecutionPolicy Bypass -File scripts\Verify-UI.ps1" -PassThru -NoNewWindow -Wait
    $sw.Stop()
    $exitCode = $proc.ExitCode
    $duration = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    
    # Audit post-run state
    $desktopCount = @(Get-Process -Name "desktop" -ErrorAction SilentlyContinue).Count
    $wvCount = @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like '*AtemDirector*' }).Count
    
    # Probe Port 8080 immediate bindability
    $port8080Free = $false
    $listener = $null
    try {
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, 8080)
        $listener.Server.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::Socket, [System.Net.Sockets.SocketOptionName]::ReuseAddress, $false)
        $listener.Start()
        $listener.Stop()
        $port8080Free = $true
    } catch {
        $port8080Free = $false
    } finally {
        if ($listener) { try { $listener.Stop() } catch { } }
    }
    
    $runPass = ($exitCode -eq 0) -and ($desktopCount -eq 0) -and ($wvCount -eq 0) -and $port8080Free
    
    $resObj = [PSCustomObject]@{
        RunNumber         = $i
        ExitCode          = $exitCode
        DurationSec       = $duration
        DesktopRemaining  = $desktopCount
        WebView2Remaining = $wvCount
        Port8080Free      = $port8080Free
        Pass              = $runPass
    }
    $results.Add($resObj)
    
    $color = if ($runPass) { "Green" } else { "Red" }
    Write-Host ">>> [RUN $i/5 RESULT] ExitCode: $exitCode, Duration: ${duration}s, DesktopProcs: $desktopCount, WV2Procs: $wvCount, Port8080Free: $port8080Free, PASS: $runPass" -ForegroundColor $color
    
    # Short interval (1s) between runs to stress test immediate re-entry and socket binding
    Start-Sleep -Seconds 1
}

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " 5 CONSECUTIVE RUNS SUMMARY RESULTS" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host

$allPassed = ($results | Where-Object { -not $_.Pass }).Count -eq 0
if ($allPassed) {
    Write-Host "ALL 5 CONSECUTIVE RUNS PASSED WITH ZERO LEAKS AND ZERO COLLISIONS!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "ONE OR MORE CONSECUTIVE RUNS FAILED!" -ForegroundColor Red
    exit 1
}
