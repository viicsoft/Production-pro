param()

$testsDll = "C:\Production-pro-main\Production-pro-main\tests\AtemDirector.Tests\bin\Debug\net9.0-windows\AtemDirector.Tests.dll"
$depsJson = "C:\Production-pro-main\Production-pro-main\tests\AtemDirector.Tests\bin\Debug\net9.0-windows\AtemDirector.Tests.deps.json"
$csproj = "C:\Production-pro-main\Production-pro-main\tests\AtemDirector.Tests\AtemDirector.Tests.csproj"

Write-Host "================================================================"
Write-Host "  DECOUPLING BOUNDARY VERIFICATION"
Write-Host "================================================================"

# Check 1: csproj
Write-Host "`n[Check 1] Inspecting csproj ProjectReferences..."
$projContent = Get-Content $csproj -Raw
if ($projContent -match "desktop") {
    Write-Error "FAIL: AtemDirector.Tests.csproj contains reference to desktop!"
    exit 1
} else {
    Write-Host "PASS: AtemDirector.Tests.csproj has NO reference to desktop."
}

# Check 2: deps.json
Write-Host "`n[Check 2] Inspecting deps.json dependencies..."
$depsContent = Get-Content $depsJson -Raw
$depsObj = $depsContent | ConvertFrom-Json
$testDeps = $depsObj.targets.'.NETCoreApp,Version=v9.0'.'AtemDirector.Tests/1.0.0'.dependencies
Write-Host "Dependencies in AtemDirector.Tests/1.0.0:"
$testDeps.psobject.properties | ForEach-Object { Write-Host " - $($_.Name): $($_.Value)" }

if ($testDeps.desktop -or $depsContent -match '"desktop/1.0.0"' -or $depsContent -match '"desktop.dll"') {
    Write-Error "FAIL: deps.json contains desktop reference!"
    exit 1
} else {
    Write-Host "PASS: deps.json has NO desktop reference."
}

# Check 3: Assembly metadata using AssemblyName or MetadataLoadContext
Write-Host "`n[Check 3] Inspecting Assembly References in binary..."
# Use IL/Metadata inspection via PEReader or string search
$bytes = [System.IO.File]::ReadAllBytes($testsDll)
$text = [System.Text.Encoding]::ASCII.GetString($bytes)

# Check for "desktop" as an assembly ref name
# In .NET assembly metadata, referenced assembly names appear as UTF-8 strings
$hasDesktopAssemblyRef = $text.Contains("desktop, Version=") -or $text.Contains("desktop.dll")
Write-Host "Binary contains 'desktop.dll' literal: $hasDesktopAssemblyRef"
if ($hasDesktopAssemblyRef) {
    Write-Error "FAIL: Binary AtemDirector.Tests.dll references desktop.dll!"
    exit 1
} else {
    Write-Host "PASS: Binary AtemDirector.Tests.dll contains NO reference to desktop.dll."
}

Write-Host "`n================================================================"
Write-Host "  VERDICT: DECOUPLING VERIFIED (APPROVE)"
Write-Host "================================================================"
exit 0
