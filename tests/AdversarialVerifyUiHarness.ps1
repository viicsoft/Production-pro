# Adversarial UI Verification Stress Harness
# Run from repository root: powershell -ExecutionPolicy Bypass -File tests\AdversarialVerifyUiHarness.ps1

$ErrorActionPreference = "Continue"

Write-Host "================================================================" -ForegroundColor Magenta
Write-Host "  ADVERSARIAL STRESS TEST HARNESS FOR Verify-UI.ps1" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

function Record-Result {
    param($Suite, $TestName, $Pass, $Details)
    $obj = [PSCustomObject]@{
        Suite = $Suite
        TestName = $TestName
        Pass = $Pass
        Details = $Details
    }
    $results.Add($obj)
    $color = if ($Pass) { "Green" } else { "Red" }
    Write-Host "[$Suite] $TestName : $(if ($Pass) { 'PASS' } else { 'FAIL' }) - $Details" -ForegroundColor $color
}

# -------------------------------------------------------------
# SUITE 1: Repeated Execution & Connection Flakiness (5 iterations)
# -------------------------------------------------------------
Write-Host "`n--- Running Suite 1: Repeated Consecutive Execution (5 runs) ---" -ForegroundColor Cyan
for ($i = 1; $i -le 5; $i++) {
    Write-Host "Starting iteration $i/5..." -ForegroundColor Gray
    $output = powershell -ExecutionPolicy Bypass -File scripts\Verify-UI.ps1 2>&1
    $ec = $LASTEXITCODE
    $outStr = ($output | Out-String)
    
    $connected = $outStr -match "StatusText after connection: 'Connected'"
    $disconnected = $outStr -match "StatusText after connection: 'Disconnected'"
    $menuBarFailed = $outStr -match "Failed to find MenuBar control in MainWindow"
    $comException = $outStr -match "0x8000FFFF"
    $cleanExit = ($ec -eq 0)

    $detail = "ExitCode=$ec, Connected=$connected, Disconnected=$disconnected, MenuBarFailed=$menuBarFailed, COMException=$comException"
    
    # Check for failure or false positive
    $isReliable = ($ec -eq 0) -and $connected -and (-not $disconnected) -and (-not $menuBarFailed) -and (-not $comException)
    Record-Result -Suite "Suite1-RepeatedRuns" -TestName "Iteration_$i" -Pass $isReliable -Details $detail
    # Allow 2.0s settling time for OS sockets (port 8080 TIME_WAIT) and WebView2 cache reclamation
    Start-Sleep -Seconds 2
}

# -------------------------------------------------------------
# SUITE 2: Invalid / Missing -ExePath Handling
# -------------------------------------------------------------
Write-Host "`n--- Running Suite 2: Invalid / Missing Executable Handling ---" -ForegroundColor Cyan

# Test 2A: Explicit non-existent path when local desktop.exe is present
$out2A = powershell -ExecutionPolicy Bypass -File scripts\Verify-UI.ps1 -ExePath "C:\totally_fake_path\missing.exe" 2>&1
$ec2A = $LASTEXITCODE
$str2A = ($out2A | Out-String)
$fellBackToCwd = $str2A -match "Target executable: .*desktop.exe"
Record-Result -Suite "Suite2-InvalidExe" -TestName "ExplicitInvalidExe_FallbackMasking" -Pass (-not $fellBackToCwd) -Details "Script masked invalid ExePath by falling back to cwd: $fellBackToCwd (ExitCode=$ec2A)"

# Test 2B: Genuine missing executable (simulated by passing nonexistent path from dir where cwd has no desktop.exe)
$out2B = powershell -ExecutionPolicy Bypass -Command "& { Set-Location C:\; & '$PSScriptRoot\..\scripts\Verify-UI.ps1' -ExePath 'C:\nonexistent.exe' 2>&1; exit `$LASTEXITCODE }"
$ec2B = $LASTEXITCODE
$str2B = ($out2B | Out-String)
$failedFast = ($ec2B -ne 0) -and ($str2B -match "Target executable not found")
Record-Result -Suite "Suite2-InvalidExe" -TestName "GenuinelyMissingExe_FailFast" -Pass $failedFast -Details "ExitCode=$ec2B, Failed fast with error: $failedFast"

# -------------------------------------------------------------
# SUITE 3: Lingering Process Cleanup
# -------------------------------------------------------------
Write-Host "`n--- Running Suite 3: Lingering Process Cleanup ---" -ForegroundColor Cyan

$procPath = [System.IO.Path]::GetFullPath("desktop\bin\Debug\net9.0-windows\desktop.exe")
if (Test-Path $procPath) {
    # Launch pre-existing process
    $lingering = Start-Process -FilePath $procPath -PassThru
    $lingeringId = $lingering.Id
    Start-Sleep -Seconds 2
    Write-Host "Spawned lingering desktop process PID: $lingeringId" -ForegroundColor Gray

    # Run Verify-UI.ps1
    $out3 = powershell -ExecutionPolicy Bypass -File scripts\Verify-UI.ps1 2>&1
    $ec3 = $LASTEXITCODE

    # Verify lingering process was killed
    $lingeringAlive = (Get-Process -Id $lingeringId -ErrorAction SilentlyContinue) -ne $null
    Record-Result -Suite "Suite3-Cleanup" -TestName "PreExistingProcessTerminated" -Pass (-not $lingeringAlive) -Details "Lingering PID $lingeringId terminated: $(-not $lingeringAlive)"

    # Verify no orphan desktop processes remain
    $remainingDesktop = @(Get-Process -Name "desktop" -ErrorAction SilentlyContinue)
    $noZombies = ($remainingDesktop.Count -eq 0)
    Record-Result -Suite "Suite3-Cleanup" -TestName "NoZombieProcessesRemaining" -Pass $noZombies -Details "Remaining desktop processes: $($remainingDesktop.Count)"
} else {
    Record-Result -Suite "Suite3-Cleanup" -TestName "PreExistingProcessTerminated" -Pass $false -Details "desktop.exe not found to test"
}

# -------------------------------------------------------------
# SUITE 4: Timeout Behavior & Failure Exit Codes
# -------------------------------------------------------------
Write-Host "`n--- Running Suite 4: Timeout Behavior ---" -ForegroundColor Cyan

# Test 4A: Timeout enforcement on non-responsive app
$timeoutSw = [System.Diagnostics.Stopwatch]::StartNew()
$out4A = powershell -ExecutionPolicy Bypass -File scripts\Verify-UI.ps1 -ExePath "C:\Windows\System32\notepad.exe" -TimeoutSeconds 3 2>&1
$timeoutSw.Stop()
$ec4A = $LASTEXITCODE
$str4A = ($out4A | Out-String)
$timeoutEnforced = ($str4A -match "failed to appear within \d+ seconds") -and ($ec4A -ne 0)
Record-Result -Suite "Suite4-Timeout" -TestName "TimeoutDeadlineEnforced" -Pass $timeoutEnforced -Details "Duration=$([Math]::Round($timeoutSw.Elapsed.TotalSeconds, 1))s, ExitCode=$ec4A, Enforced=$timeoutEnforced"

# Check if notepad was cleanly terminated
$notepadZombies = @(Get-Process -Name "notepad" -ErrorAction SilentlyContinue | Where-Object {
    try {
        if (-not $_.HasExited) {
            $st = $_.StartTime
            if ($st -and ($st -gt [DateTime]::UtcNow.AddSeconds(-30))) { return $true }
        }
    } catch { }
    return $false
})
$notepadCleaned = ($notepadZombies.Count -eq 0)
Record-Result -Suite "Suite4-Timeout" -TestName "TimeoutProcessCleanedUp" -Pass $notepadCleaned -Details "Notepad process cleaned up: $notepadCleaned"

# -------------------------------------------------------------
# SUMMARY REPORT
# -------------------------------------------------------------
Write-Host "`n================================================================" -ForegroundColor Magenta
Write-Host "  ADVERSARIAL HARNESS SUMMARY RESULTS" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta

$passed = @($results | Where-Object { $_.Pass -eq $true }).Count
$failed = @($results | Where-Object { $_.Pass -eq $false }).Count
$total = $results.Count

Write-Host "Total Tests : $total"
Write-Host "Passed      : $passed" -ForegroundColor Green
Write-Host "Failed      : $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })

$results | Format-Table -AutoSize | Out-String | Write-Host

if ($failed -gt 0) {
    Write-Host "VERDICT: CHALLENGE_FAILED" -ForegroundColor Red
    exit 1
} else {
    Write-Host "VERDICT: APPROVE" -ForegroundColor Green
    exit 0
}
