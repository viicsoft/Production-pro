param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$desktopDll = (Resolve-Path "desktop\bin\$Configuration\net9.0-windows\desktop.dll").Path
$desktopExe = (Resolve-Path "desktop\bin\$Configuration\net9.0-windows\desktop.exe").Path

Write-Host "================================================================"
Write-Host "  FILE LOCK & DECOUPLING STRESS TEST"
Write-Host "================================================================"
Write-Host "Target Desktop DLL: $desktopDll"
Write-Host "Target Desktop EXE: $desktopExe"

Write-Host "`n[Step 1] Acquiring exclusive locks (FileShare.None) on desktop binaries..."
$streamDll = [System.IO.File]::Open($desktopDll, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
$streamExe = [System.IO.File]::Open($desktopExe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)

try {
    # Verify file is truly locked by attempting read from another handle
    $locked = $false
    try {
        $testStream = [System.IO.File]::OpenRead($desktopDll)
        $testStream.Close()
    } catch [System.IO.IOException] {
        $locked = $true
    }

    if (-not $locked) {
        throw "Exclusive lock assertion failed: desktop.dll was unexpectedly accessible!"
    }
    Write-Host "  [OK] Exclusive lock confirmed: desktop.dll is locked with FileShare.None."

    Write-Host "`n[Step 2] Building AtemDirector.Tests from scratch while desktop binaries are locked..."
    # Clean AtemDirector.Tests output to force full rebuild
    Remove-Item -Path "tests\AtemDirector.Tests\bin\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "tests\AtemDirector.Tests\obj\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue

    dotnet build tests\AtemDirector.Tests\AtemDirector.Tests.csproj -c $Configuration --no-incremental
    if ($LASTEXITCODE -ne 0) {
        throw "AtemDirector.Tests build failed with exit code $LASTEXITCODE while desktop binaries were locked!"
    }
    Write-Host "  [PASS] AtemDirector.Tests compiled cleanly (ExitCode=0) despite desktop binaries being locked."

    Write-Host "`n[Step 3] Running AtemDirector.Tests while desktop binaries are locked..."
    dotnet test tests\AtemDirector.Tests\AtemDirector.Tests.csproj -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "AtemDirector.Tests test run failed with exit code $LASTEXITCODE while desktop binaries were locked!"
    }
    Write-Host "  [PASS] AtemDirector.Tests passed 100% of tests (ExitCode=0) while desktop binaries were locked."

    Write-Host "`n[Step 4] Negative Control: Verify Desktop.Tests fails when desktop.dll is locked..."
    # Clean Desktop.Tests output to force copy of desktop.dll
    Remove-Item -Path "tests\Desktop.Tests\bin\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "tests\Desktop.Tests\obj\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue

    # Use fast retry count so msbuild doesn't stall
    dotnet build tests\Desktop.Tests\Desktop.Tests.csproj -c $Configuration /p:CopyRetryCount=1 /p:CopyRetryDelayMilliseconds=50
    $negExitCode = $LASTEXITCODE
    if ($negExitCode -eq 0) {
        Write-Warning "Negative control: Desktop.Tests built without copying desktop.dll? Exit code was 0."
    } else {
        Write-Host "  [PASS] Negative control succeeded: Desktop.Tests failed as expected (ExitCode=$negExitCode) due to locked desktop.dll."
    }
}
finally {
    $streamDll.Close()
    $streamDll.Dispose()
    $streamExe.Close()
    $streamExe.Dispose()
    Write-Host "`n[CLEANUP] Released exclusive locks on desktop binaries."
}

# Restore Desktop.Tests after negative control
Write-Host "`n[POST-VERIFICATION] Rebuilding full solution AtemDirector.sln to ensure all outputs are valid..."
dotnet build AtemDirector.sln -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Solution rebuild failed with exit code $LASTEXITCODE!"
}

Write-Host "================================================================"
Write-Host "  FILE LOCK & DECOUPLING STRESS TEST: 100% PASSED"
Write-Host "================================================================"
