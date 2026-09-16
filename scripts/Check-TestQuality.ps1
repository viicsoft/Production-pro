param()

$testsDir = "C:\Production-pro-main\Production-pro-main\tests"
$csFiles = Get-ChildItem -Path $testsDir -Filter "*.cs" -Recurse | Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }

Write-Host "================================================================"
Write-Host "  TEST SUITE QUALITY AUDIT: SKIPS & TIMEOUTS"
Write-Host "================================================================"
Write-Host "Found $($csFiles.Count) test source files."

$skipsFound = @()
$sleepsFound = @()
$delaysFound = @()
$tautologiesFound = @()

foreach ($file in $csFiles) {
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineNum = $i + 1
        $line = $lines[$i]

        # Check for Skips
        if ($line -match 'Skip\s*=' -or $line -match 'Assert\.Skip') {
            $skipsFound += [PSCustomObject]@{ File = $file.Name; Line = $lineNum; Content = $line.Trim() }
        }

        # Check for Thread.Sleep
        if ($line -match 'Thread\.Sleep') {
            $sleepsFound += [PSCustomObject]@{ File = $file.Name; Line = $lineNum; Content = $line.Trim() }
        }

        # Check for Task.Delay
        if ($line -match 'Task\.Delay') {
            $delaysFound += [PSCustomObject]@{ File = $file.Name; Line = $lineNum; Content = $line.Trim() }
        }

        # Check for tautologies like Assert.True(true)
        if ($line -match 'Assert\.True\s*\(\s*true\s*\)' -or $line -match 'Assert\.False\s*\(\s*false\s*\)') {
            $tautologiesFound += [PSCustomObject]@{ File = $file.Name; Line = $lineNum; Content = $line.Trim() }
        }
    }
}

Write-Host "`n--- 1. SKIPS CHECK ---"
if ($skipsFound.Count -eq 0) {
    Write-Host "PASS: 0 skipped tests found across all suites."
} else {
    Write-Warning "FOUND SKIPPED TESTS: $($skipsFound.Count)"
    $skipsFound | Format-Table -AutoSize
}

Write-Host "`n--- 2. THREAD.SLEEP CHECK ---"
if ($sleepsFound.Count -eq 0) {
    Write-Host "PASS: 0 instances of Thread.Sleep found across all test files."
} else {
    Write-Warning "FOUND Thread.Sleep: $($sleepsFound.Count)"
    $sleepsFound | Format-Table -AutoSize
}

Write-Host "`n--- 3. TASK.DELAY CHECK ---"
if ($delaysFound.Count -eq 0) {
    Write-Host "PASS: 0 instances of Task.Delay found."
} else {
    Write-Host "Found $($delaysFound.Count) instances of Task.Delay. Inspecting usage patterns:"
    $delaysFound | Format-Table -AutoSize
}

Write-Host "`n--- 4. TAUTOLOGY CHECK (Assert.True(true)) ---"
if ($tautologiesFound.Count -eq 0) {
    Write-Host "PASS: 0 tautological assertions found across all test files."
} else {
    Write-Warning "FOUND TAUTOLOGIES: $($tautologiesFound.Count)"
    $tautologiesFound | Format-Table -AutoSize
}

Write-Host "`n================================================================"
if ($skipsFound.Count -eq 0 -and $sleepsFound.Count -eq 0 -and $tautologiesFound.Count -eq 0) {
    Write-Host "AUDIT RESULT: CLEAN - NO SKIPS OR ARTIFICIAL THREAD SLEEPS"
} else {
    Write-Host "AUDIT RESULT: DEFECTS DETECTED"
}
Write-Host "================================================================"
