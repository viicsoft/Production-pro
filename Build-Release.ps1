# AtemDirector Build Script
# Builds self-contained deployments for Server and Desktop

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AtemDirector Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean previous builds
Write-Host "[1/5] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path ".\publish") {
    Remove-Item ".\publish" -Recurse -Force
}
New-Item -ItemType Directory -Path ".\publish" | Out-Null
New-Item -ItemType Directory -Path ".\publish\caddy" | Out-Null
New-Item -ItemType Directory -Path ".\publish\server" | Out-Null
New-Item -ItemType Directory -Path ".\publish\desktop" | Out-Null

# Build Server (self-contained)
Write-Host "[2/5] Building Server (self-contained)..." -ForegroundColor Yellow
Set-Location ".\server"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "..\publish\server"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Server build failed!" -ForegroundColor Red
    exit 1
}
Set-Location ".."

# Build Desktop (self-contained)
Write-Host "[3/5] Building Desktop (self-contained)..." -ForegroundColor Yellow
Set-Location ".\desktop"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "..\publish\desktop"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Desktop build failed!" -ForegroundColor Red
    exit 1
}
Set-Location ".."

# Copy Caddy
Write-Host "[4/5] Copying Caddy..." -ForegroundColor Yellow
$caddyFound = $false
$caddyCandidates = @(
    ".\installer\caddy\caddy.exe",
    "C:\Program Files\AtemDirector\caddy\caddy.exe",
    "C:\caddy\caddy.exe"
)

foreach ($path in $caddyCandidates) {
    if (Test-Path $path) {
        Copy-Item $path ".\publish\caddy\caddy.exe" -Force
        $caddyFound = $true
        Write-Host "Copied caddy.exe from: $path" -ForegroundColor Green
        break
    }
}

if (-not $caddyFound) {
    Write-Host "Warning: Caddy.exe not found in known locations!" -ForegroundColor Yellow
    Write-Host "Please copy caddy.exe manually to .\publish\caddy\" -ForegroundColor Yellow
}

# Create Universal Caddyfile (listens on port 8443 across all local network interfaces without prompting)
$caddyConfig = @"
:8443 {
    tls internal
    reverse_proxy 127.0.0.1:8080
}
"@
$caddyConfig | Set-Content ".\publish\caddy\Caddyfile"

# Copy ATEM SDK DLLs
Write-Host "[5/5] Copying ATEM SDK DLLs..." -ForegroundColor Yellow
Copy-Item ".\desktop\lib\*.dll" ".\publish\desktop\" -Force

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output directory: .\publish\" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Run Build-Installer.ps1 to create the installer" -ForegroundColor White
Write-Host "  2. Or manually copy .\publish\ folder to target machine" -ForegroundColor White
Write-Host ""
