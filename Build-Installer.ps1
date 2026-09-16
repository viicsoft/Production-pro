# Build Installer Script
# Requires Inno Setup to be installed

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AtemDirector Installer Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if Inno Setup is installed
$innoSetupPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$localInnoSetupPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"

if (Test-Path $localInnoSetupPath) {
    $innoSetupPath = $localInnoSetupPath
}

if (-not (Test-Path $innoSetupPath)) {
    Write-Host "ERROR: Inno Setup not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Inno Setup from:" -ForegroundColor Yellow
    Write-Host "  https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "After installation, run this script again." -ForegroundColor Yellow
    exit 1
}

# Check if publish folder exists
if (-not (Test-Path ".\publish")) {
    Write-Host "ERROR: .\publish folder not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please run Build-Release.ps1 first to build the application." -ForegroundColor Yellow
    exit 1
}

# Create installer-output directory
if (-not (Test-Path ".\installer-output")) {
    New-Item -ItemType Directory -Path ".\installer-output" | Out-Null
}

# Build installer
Write-Host "Building installer..." -ForegroundColor Yellow
Write-Host ""

& $innoSetupPath ".\installer\AtemDirector.iss"

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Installer Built Successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    
    $installerFile = Get-ChildItem ".\installer-output\AtemDirector-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($installerFile) {
        $fileSize = [math]::Round($installerFile.Length / 1MB, 2)
        Write-Host "Installer: $($installerFile.Name)" -ForegroundColor Cyan
        Write-Host "Size: $fileSize MB" -ForegroundColor Cyan
        Write-Host "Location: $($installerFile.FullName)" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "You can now distribute this installer!" -ForegroundColor Green
    }
} else {
    Write-Host ""
    Write-Host "Installer build failed!" -ForegroundColor Red
    exit 1
}
