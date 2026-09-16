param(
    [int]$Iterations = 5,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Continue"

Write-Host "================================================================"
Write-Host "  AtemDirector.Tests INDEPENDENT SOAK TEST ($Iterations RUNS)"
Write-Host "================================================================"

$passes = 0
$results = @()

for ($i = 1; $i -le $Iterations; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $out = dotnet test tests\AtemDirector.Tests\AtemDirector.Tests.csproj -c $Configuration --no-build
    $code = $LASTEXITCODE
    $sw.Stop()
    
    $outStr = $out -join "`n"
    $passed = 0; $failed = 0; $skipped = 0
    if ($outStr -match 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+)') {
        $failed = [int]$matches[1]
        $passed = [int]$matches[2]
        $skipped = [int]$matches[3]
    }
    
    $res = [PSCustomObject]@{
        Run = $i
        Duration = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
        ExitCode = $code
        Status = if ($code -eq 0 -and $failed -eq 0) { "PASS" } else { "FAIL" }
    }
    $results += $res
    if ($res.Status -eq "PASS") { $passes++ }
    
    Write-Host "Run $i : Status=$($res.Status), Passed=$passed, Failed=$failed, Skipped=$skipped, Time=$($res.Duration)s, ExitCode=$code"
}

Write-Host "`nSummary: $passes of $Iterations runs PASSED."
$results | Format-Table -AutoSize | Out-String | Write-Host
if ($passes -eq $Iterations) {
    Write-Host "VERDICT: APPROVE (AtemDirector.Tests has 0 flakiness and 100% stability)"
    exit 0
} else {
    Write-Host "VERDICT: CHALLENGE_FAILED"
    exit 1
}
