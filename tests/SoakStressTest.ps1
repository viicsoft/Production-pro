param(
    [int]$Iterations = 5,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================"
Write-Host "  SOAK & CONCURRENCY STRESS TEST HARNESS"
Write-Host "================================================================"
Write-Host "Target Solution: AtemDirector.sln"
Write-Host "Configuration:   $Configuration"
Write-Host "Soak Iterations: $Iterations"
Write-Host "Timestamp:       $([System.DateTime]::UtcNow.ToString('o'))"
Write-Host "================================================================"

# First ensure solution is freshly built
Write-Host "`n[PRE-RUN] Building AtemDirector.sln..."
dotnet build AtemDirector.sln -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Initial solution build failed with code $LASTEXITCODE"
}
Write-Host "Initial build completed successfully."

# =========================================================================
# SUITE 1: Sequential Soak Runs (5+ Iterations)
# =========================================================================
Write-Host "`n================================================================"
Write-Host "  SUITE 1: REPEATED SEQUENTIAL SOAK RUNS ($Iterations ITERATIONS)"
Write-Host "================================================================"

$soakResults = @()

for ($i = 1; $i -le $Iterations; $i++) {
    Write-Host "`n--- [Soak Iteration $i of $Iterations] Starting dotnet test AtemDirector.sln ---"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    
    $output = dotnet test AtemDirector.sln -c $Configuration --no-build 2>&1
    $exitCode = $LASTEXITCODE
    $sw.Stop()
    
    $outputStr = $output -join "`n"
    
    # Parse test counts
    $passed = 0
    $failed = 0
    $skipped = 0
    
    # Matches patterns like: Passed!  - Failed: 0, Passed: 256, Skipped: 0, Total: 256
    $matches = [regex]::Matches($outputStr, 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+)')
    foreach ($m in $matches) {
        $failed += [int]$m.Groups[1].Value
        $passed += [int]$m.Groups[2].Value
        $skipped += [int]$m.Groups[3].Value
    }
    
    $result = [PSCustomObject]@{
        Iteration = $i
        DurationSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        ExitCode = $exitCode
        TotalPassed = $passed
        TotalFailed = $failed
        TotalSkipped = $skipped
        Status = if ($exitCode -eq 0 -and $failed -eq 0) { "PASS" } else { "FAIL" }
    }
    $soakResults += $result
    
    Write-Host "Iteration $i Result: Status=$($result.Status), Duration=$($result.DurationSeconds)s, Passed=$passed, Failed=$failed, Skipped=$skipped, ExitCode=$exitCode"
    
    if ($result.Status -ne "PASS") {
        Write-Error "Soak iteration $i failed! Halting."
        break
    }
}

# =========================================================================
# SUITE 2: Concurrent Parallel Execution Stress Test
# =========================================================================
Write-Host "`n================================================================"
Write-Host "  SUITE 2: CONCURRENT PARALLEL EXECUTION (STATE ISOLATION)"
Write-Host "================================================================"
Write-Host "Launching AtemDirector.Tests and Desktop.Tests concurrently in parallel..."

$concurrentRuns = 3
$concurrentResults = @()

for ($c = 1; $c -le $concurrentRuns; $c++) {
    Write-Host "`n--- [Concurrency Run $c of $concurrentRuns] Simultaneous Execution ---"
    $swC = [System.Diagnostics.Stopwatch]::StartNew()
    
    $job1 = Start-Job -ScriptBlock {
        param($config)
        Set-Location "C:\Production-pro-main\Production-pro-main"
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $out = dotnet test tests\AtemDirector.Tests\AtemDirector.Tests.csproj -c $config --no-build 2>&1
        $code = $LASTEXITCODE
        $sw.Stop()
        return [PSCustomObject]@{
            Project = "AtemDirector.Tests"
            ExitCode = $code
            Duration = $sw.Elapsed.TotalSeconds
            Output = ($out -join "`n")
        }
    } -ArgumentList $Configuration
    
    $job2 = Start-Job -ScriptBlock {
        param($config)
        Set-Location "C:\Production-pro-main\Production-pro-main"
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $out = dotnet test tests\Desktop.Tests\Desktop.Tests.csproj -c $config --no-build 2>&1
        $code = $LASTEXITCODE
        $sw.Stop()
        return [PSCustomObject]@{
            Project = "Desktop.Tests"
            ExitCode = $code
            Duration = $sw.Elapsed.TotalSeconds
            Output = ($out -join "`n")
        }
    } -ArgumentList $Configuration
    
    Wait-Job -Job @($job1, $job2) | Out-Null
    $res1 = Receive-Job -Job $job1
    $res2 = Receive-Job -Job $job2
    Remove-Job -Job @($job1, $job2) -Force
    $swC.Stop()
    
    $passed1 = 0; $failed1 = 0
    if ($res1.Output -match 'Failed:\s*(\d+),\s*Passed:\s*(\d+)') {
        $failed1 = [int]$matches[1]
        $passed1 = [int]$matches[2]
    }
    
    $passed2 = 0; $failed2 = 0
    if ($res2.Output -match 'Failed:\s*(\d+),\s*Passed:\s*(\d+)') {
        $failed2 = [int]$matches[1]
        $passed2 = [int]$matches[2]
    }
    
    $status1 = if ($res1.ExitCode -eq 0 -and $failed1 -eq 0) { "PASS" } else { "FAIL" }
    $status2 = if ($res2.ExitCode -eq 0 -and $failed2 -eq 0) { "PASS" } else { "FAIL" }
    
    Write-Host "  Parallel Job 1 (AtemDirector.Tests): Status=$status1, Passed=$passed1, Failed=$failed1, Time=$([math]::Round($res1.Duration, 1))s"
    Write-Host "  Parallel Job 2 (Desktop.Tests):      Status=$status2, Passed=$passed2, Failed=$failed2, Time=$([math]::Round($res2.Duration, 1))s"
    Write-Host "  Total Concurrency Round $c Duration: $([math]::Round($swC.Elapsed.TotalSeconds, 1))s"
    
    $concurrentResults += [PSCustomObject]@{
        Round = $c
        Job1Status = $status1
        Job1Passed = $passed1
        Job2Status = $status2
        Job2Passed = $passed2
        Elapsed = [math]::Round($swC.Elapsed.TotalSeconds, 2)
    }
}

# =========================================================================
# SUMMARY & VERDICT
# =========================================================================
Write-Host "`n================================================================"
Write-Host "  SOAK & CONCURRENCY STRESS TEST SUMMARY"
Write-Host "================================================================"

Write-Host "`n--- Sequential Soak Run Table ---"
$soakResults | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- Concurrent Execution Table ---"
$concurrentResults | Format-Table -AutoSize | Out-String | Write-Host

$allSoakPassed = ($soakResults.Count -eq $Iterations) -and (($soakResults | Where-Object { $_.Status -ne "PASS" }).Count -eq 0)
$allConcurrentPassed = ($concurrentResults.Count -eq $concurrentRuns) -and (($concurrentResults | Where-Object { $_.Job1Status -ne "PASS" -or $_.Job2Status -ne "PASS" }).Count -eq 0)

if ($allSoakPassed -and $allConcurrentPassed) {
    Write-Host "`nVERDICT: APPROVE"
    Write-Host "Zero flakiness observed. 100% pass across all sequential soak runs and parallel concurrent executions."
    exit 0
} else {
    Write-Host "`nVERDICT: CHALLENGE_FAILED"
    Write-Host "One or more stress test runs failed or exhibited flakiness."
    exit 1
}
