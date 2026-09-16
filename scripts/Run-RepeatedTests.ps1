param(
    [int]$Iterations = 3
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================"
Write-Host "  REPEATED TEST EXECUTION HARNESS ($Iterations Consecutive Runs)"
Write-Host "================================================================"

$results = @()

for ($i = 1; $i -le $Iterations; $i++) {
    Write-Host "`n>>> RUN $i / ${Iterations}: AtemDirector.Tests.csproj <<<"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = dotnet test tests\AtemDirector.Tests\AtemDirector.Tests.csproj --no-restore
    $sw.Stop()
    $exitCode = $LASTEXITCODE
    
    $outputText = $output -join "`n"
    Write-Host $outputText

    $matched = $outputText -match 'Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)'
    $failed = if ($matched) { [int]$Matches[1] } else { -1 }
    $passed = if ($matched) { [int]$Matches[2] } else { -1 }
    $skipped = if ($matched) { [int]$Matches[3] } else { -1 }
    $total = if ($matched) { [int]$Matches[4] } else { -1 }

    $pass = ($exitCode -eq 0) -and ($failed -eq 0) -and ($skipped -eq 0) -and ($passed -eq 178)
    $results += [PSCustomObject]@{
        Suite = "AtemDirector.Tests"
        Iteration = $i
        ExitCode = $exitCode
        Failed = $failed
        Passed = $passed
        Skipped = $skipped
        Total = $total
        DurationSec = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        Result = if ($pass) { "PASS" } else { "FAIL" }
    }
}

for ($i = 1; $i -le $Iterations; $i++) {
    Write-Host "`n>>> RUN $i / ${Iterations}: Desktop.Tests.csproj <<<"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = dotnet test tests\Desktop.Tests\Desktop.Tests.csproj --no-build
    $sw.Stop()
    $exitCode = $LASTEXITCODE

    $outputText = $output -join "`n"
    Write-Host $outputText

    $matched = $outputText -match 'Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)'
    $failed = if ($matched) { [int]$Matches[1] } else { -1 }
    $passed = if ($matched) { [int]$Matches[2] } else { -1 }
    $skipped = if ($matched) { [int]$Matches[3] } else { -1 }
    $total = if ($matched) { [int]$Matches[4] } else { -1 }

    $pass = ($exitCode -eq 0) -and ($failed -eq 0) -and ($skipped -eq 0) -and ($passed -eq 222)
    $results += [PSCustomObject]@{
        Suite = "Desktop.Tests"
        Iteration = $i
        ExitCode = $exitCode
        Failed = $failed
        Passed = $passed
        Skipped = $skipped
        Total = $total
        DurationSec = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        Result = if ($pass) { "PASS" } else { "FAIL" }
    }
}

Write-Host "`n================================================================"
Write-Host "  REPEATED TEST SUMMARY"
Write-Host "================================================================"
$results | Format-Table -AutoSize

$anyFailure = $results | Where-Object { $_.Result -ne "PASS" }
if ($anyFailure) {
    Write-Error "FAIL: One or more test runs failed or exhibited flakiness!"
    exit 1
} else {
    Write-Host "SUCCESS: All repeated runs passed with 100% success rate, 0 flakes, 0 skips."
    exit 0
}
