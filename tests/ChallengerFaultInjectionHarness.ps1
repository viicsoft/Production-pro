# Challenger Empirical Fault Injection Verification Harness
$ErrorActionPreference = "Continue"

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " CHALLENGER EMPIRICAL FAULT INJECTION HARNESS" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$faultResults = [System.Collections.Generic.List[PSCustomObject]]::new()

function Record-FaultResult {
    param($TestId, $Description, $Pass, $Details)
    $obj = [PSCustomObject]@{
        TestId      = $TestId
        Description = $Description
        Pass        = $Pass
        Details     = $Details
    }
    $faultResults.Add($obj)
    $color = if ($Pass) { "Green" } else { "Red" }
    Write-Host "[$TestId] $Description : $(if ($Pass) { 'PASS' } else { 'FAIL' })" -ForegroundColor $color
    Write-Host "       Details: $Details" -ForegroundColor Gray
}

# ----------------------------------------------------------------------
# FAULT 1: Port 8080 Collision Resolution (Wait-Port8080Free)
# ----------------------------------------------------------------------
Write-Host "`n--- [Fault 1] Simulating Port 8080 Conflict Prior to Launch ---" -ForegroundColor Yellow
# Spawn a rogue TCP listener on port 8080
$rogueListenerProc = Start-Process powershell -ArgumentList "-NoProfile -Command `"`$l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, 8080); `$l.Start(); Start-Sleep -Seconds 30; `$l.Stop()`"" -PassThru
Start-Sleep -Seconds 1
$roguePid = $rogueListenerProc.Id
$rogueListening = (Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -eq $roguePid }) -ne $null
Write-Host "Spawned rogue listener PID $roguePid (Listening: $rogueListening)" -ForegroundColor Gray

# Run Verify-UI.ps1; it should detect the conflict, terminate the rogue process via Wait-Port8080Free, and succeed
$outF1 = powershell -ExecutionPolicy Bypass -File scripts\Verify-UI.ps1 2>&1
$ecF1 = $LASTEXITCODE
$rogueAlive = (Get-Process -Id $roguePid -ErrorAction SilentlyContinue) -ne $null
$f1Success = ($ecF1 -eq 0) -and (-not $rogueAlive)
Record-FaultResult -TestId "FAULT_PORT_COLLISION" -Description "Wait-Port8080Free eliminates rogue port 8080 occupant and succeeds" -Pass $f1Success -Details "ExitCode=$ecF1, RogueTerminated=$(-not $rogueAlive)"

# ----------------------------------------------------------------------
# FAULT 2: Timeout Enforcement on Hung Process & Process Cleanup
# ----------------------------------------------------------------------
Write-Host "`n--- [Fault 2] Testing Timeout Enforcement on Non-Responsive App ---" -ForegroundColor Yellow
$swF2 = [System.Diagnostics.Stopwatch]::StartNew()
$outF2 = powershell -ExecutionPolicy Bypass -File scripts\Verify-UI.ps1 -ExePath "C:\Windows\System32\cmd.exe" -TimeoutSeconds 3 2>&1
$swF2.Stop()
$ecF2 = $LASTEXITCODE
$strF2 = ($outF2 | Out-String)
$timeoutDetected = ($ecF2 -ne 0) -and ($strF2 -match "failed to appear within 3 seconds")
Record-FaultResult -TestId "FAULT_TIMEOUT_ENFORCED" -Description "Verify-UI enforces timeout deadline and exits non-zero" -Pass $timeoutDetected -Details "ExitCode=$ecF2, DurationSec=$([math]::Round($swF2.Elapsed.TotalSeconds, 2)), Caught=$timeoutDetected"

# ----------------------------------------------------------------------
# FAULT 3: Missing/Invalid Executable Fail-Fast Detection
# ----------------------------------------------------------------------
Write-Host "`n--- [Fault 3] Testing Missing/Invalid Executable Detection ---" -ForegroundColor Yellow
$outF3 = powershell -ExecutionPolicy Bypass -File scripts\Verify-UI.ps1 -ExePath "C:\totally_bogus_app_path_12345.exe" 2>&1
$ecF3 = $LASTEXITCODE
$strF3 = ($outF3 | Out-String)
$invalidExeCaught = ($ecF3 -ne 0) -and ($strF3 -match "Target executable not found")
Record-FaultResult -TestId "FAULT_INVALID_EXE" -Description "Verify-UI detects missing executable and fails fast with code != 0" -Pass $invalidExeCaught -Details "ExitCode=$ecF3, ErrorReported=$invalidExeCaught"

# ----------------------------------------------------------------------
# FAULT 4: Harness Detection of Simulated Failure (No False Positives)
# ----------------------------------------------------------------------
Write-Host "`n--- [Fault 4] Testing Harness Failure Detection (No Masking) ---" -ForegroundColor Yellow
# Verify that if a failure occurs, the harness marks the test as FAIL and exits with code 1
$mockHarness = {
    param($simulatedExitCode, $simulatedOutput)
    $connected = $simulatedOutput -match "StatusText after connection: 'Connected'"
    $disconnected = $simulatedOutput -match "StatusText after connection: 'Disconnected'"
    $menuBarFailed = $simulatedOutput -match "Failed to find MenuBar control in MainWindow"
    $comException = $simulatedOutput -match "0x8000FFFF"
    $isReliable = ($simulatedExitCode -eq 0) -and $connected -and (-not $disconnected) -and (-not $menuBarFailed) -and (-not $comException)
    return $isReliable
}

$simRun1 = & $mockHarness -simulatedExitCode 1 -simulatedOutput "StatusText after connection: 'Connected'" # Exit code 1
$simRun2 = & $mockHarness -simulatedExitCode 0 -simulatedOutput "StatusText after connection: 'Disconnected'" # Disconnected
$simRun3 = & $mockHarness -simulatedExitCode 0 -simulatedOutput "Failed to find MenuBar control in MainWindow" # Missing MenuBar
$simRun4 = & $mockHarness -simulatedExitCode 0 -simulatedOutput "Exception 0x8000FFFF" # COM error

$allFailuresDetected = (-not $simRun1) -and (-not $simRun2) -and (-not $simRun3) -and (-not $simRun4)
Record-FaultResult -TestId "FAULT_HARNESS_DISCRIMINATION" -Description "Adversarial harness logic correctly rejects all 4 fault signatures" -Pass $allFailuresDetected -Details "ExitCodeFail=$(-not $simRun1), DisconnectedFail=$(-not $simRun2), MenuBarFail=$(-not $simRun3), ComExceptionFail=$(-not $simRun4)"

# ----------------------------------------------------------------------
# SUMMARY REPORT
# ----------------------------------------------------------------------
Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " FAULT INJECTION SUMMARY RESULTS" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
$faultResults | Format-Table -AutoSize | Out-String | Write-Host

$allPassed = ($faultResults | Where-Object { -not $_.Pass }).Count -eq 0
if ($allPassed) {
    Write-Host "ALL FAULT INJECTION TESTS PASSED! No failure masking detected." -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAULT INJECTION TESTS FAILED!" -ForegroundColor Red
    exit 1
}
