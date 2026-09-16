# Targeted diagnostic script to reproduce and isolate flakiness in Verify-UI.ps1
$ErrorActionPreference = "Continue"

Write-Host "Starting diagnostic repetition loop (up to 10 iterations) to capture exact failure output..."

for ($i = 1; $i -le 10; $i++) {
    Write-Host "`n--- Diagnostic Iteration $i ---" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $out = powershell -ExecutionPolicy Bypass -File scripts\Verify-UI.ps1 2>&1
    $ec = $LASTEXITCODE
    $sw.Stop()
    $outText = $out | Out-String
    
    if ($ec -ne 0) {
        Write-Host ">>> CAUGHT FAILURE IN ITERATION $i (ExitCode: $ec, Duration: $([math]::Round($sw.Elapsed.TotalSeconds, 2))s) <<<" -ForegroundColor Red
        Write-Host "--- VERBATIM SCRIPT OUTPUT ---" -ForegroundColor Yellow
        Write-Host $outText
        Write-Host "------------------------------" -ForegroundColor Yellow
        break
    } else {
        Write-Host "Iteration $i PASSED cleanly in $([math]::Round($sw.Elapsed.TotalSeconds, 2))s" -ForegroundColor Green
    }
    Start-Sleep -Seconds 2
}
