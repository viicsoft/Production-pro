$ErrorActionPreference = "Stop"

Write-Host "Searching for TlbImp.exe..." -ForegroundColor Cyan
$tlbImp = Get-ChildItem "C:\Program Files (x86)\Microsoft SDKs" -Recurse -Filter "TlbImp.exe" -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $tlbImp) {
    Write-Error "Could not find TlbImp.exe. Please ensure Windows SDK is installed."
    exit
}
Write-Host "Found TlbImp.exe at: $($tlbImp.FullName)" -ForegroundColor Green

Write-Host "Searching for BMDSwitcherAPI.tlb..." -ForegroundColor Cyan
$searchPaths = @(
    "C:\Program Files\Blackmagic Design",
    "C:\Program Files (x86)\Blackmagic Design",
    "C:\Program Files (x86)\Blackmagic Design\Blackmagic ATEM Switchers",
    "$env:USERPROFILE\Downloads\Blackmagic_ATEM_Switchers_SDK_10.1"
)

$tlbPath = $null
foreach ($path in $searchPaths) {
    if (Test-Path $path) {
        Write-Host "Checking $path..." -ForegroundColor Gray
        $found = Get-ChildItem $path -Recurse -Filter "BMDSwitcherAPI.tlb" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $tlbPath = $found
            break
        }
    }
}

if (-not $tlbPath) {
    Write-Error "Could not find BMDSwitcherAPI.tlb in Program Files or Downloads."
    exit
}
Write-Host "Found Type Library at: $($tlbPath.FullName)" -ForegroundColor Green

$outputDir = "C:\worker\AtemDirector\desktop\lib"
$outputFile = Join-Path $outputDir "Interop.BMDSwitcherAPI.MiniExtreme.dll"

Write-Host "Generating DLL..." -ForegroundColor Cyan
$argList = "`"$($tlbPath.FullName)`" /out:`"$outputFile`" /namespace:BMDSwitcherAPI /machine:x64"

Start-Process -FilePath $tlbImp.FullName -ArgumentList $argList -Wait -NoNewWindow

if (Test-Path $outputFile) {
    Write-Host "SUCCESS! Generated: $outputFile" -ForegroundColor Green
    
    # Also rename the existing one to MiniProISO if not already renamed
    $oldDll = Join-Path $outputDir "Interop.BMDSwitcherAPI.dll"
    $newOldDll = Join-Path $outputDir "Interop.BMDSwitcherAPI.MiniProISO.dll"
    
    if (Test-Path $oldDll) {
        Rename-Item $oldDll "Interop.BMDSwitcherAPI.MiniProISO.dll"
        Write-Host "Renamed existing DLL to Interop.BMDSwitcherAPI.MiniProISO.dll" -ForegroundColor Yellow
    }
} else {
    Write-Error "Failed to generate DLL."
}
