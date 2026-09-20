param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Continue"

Write-Host "Running dotnet test CuePilot.sln..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$output = dotnet test CuePilot.sln -c $Configuration --no-build
$exitCode = $LASTEXITCODE
$sw.Stop()

Write-Host "`nTest execution completed in $([math]::Round($sw.Elapsed.TotalSeconds, 1))s with ExitCode=$exitCode"
$output | Select-String -Pattern "FAIL|Total:" | ForEach-Object { Write-Host $_ }
