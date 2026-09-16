$ErrorActionPreference = "Continue"

Write-Host "Targeting AtemDirector.Tests.ChallengerConcurrencyStressTests.SimAtem_ExtremeConcurrency_MultiThreadedStress_NoExceptionsOrDeadlock..."

for ($i = 1; $i -le 10; $i++) {
    Write-Host "Starting Attempt $i..."
    $out = dotnet test tests\AtemDirector.Tests\AtemDirector.Tests.csproj --filter "FullyQualifiedName=AtemDirector.Tests.ChallengerConcurrencyStressTests.SimAtem_ExtremeConcurrency_MultiThreadedStress_NoExceptionsOrDeadlock" --logger "console;verbosity=detailed"
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Host "`n*** REPRODUCED FAILURE ON ATTEMPT $i ***"
        $out | Select-String -Pattern "Error Message|Stack Trace|Exception|Failed" -Context 0, 5 | ForEach-Object { Write-Host $_ }
        exit 1
    } else {
        Write-Host "Attempt $i passed cleanly."
    }
}
Write-Host "All 10 attempts passed."
