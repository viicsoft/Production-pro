# Vidikom Commercial Release Build Script
# Builds self-contained deployments for Server and Desktop with all native & runtime dependencies bundled

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Vidikom Commercial Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Stop any running instances that could lock files in publish/
Get-Process -Name "Vidikom","server","Server","caddy" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# Clean previous builds
Write-Host "[1/7] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path ".\publish") {
    Remove-Item ".\publish" -Recurse -Force
}
New-Item -ItemType Directory -Path ".\publish" | Out-Null
New-Item -ItemType Directory -Path ".\publish\caddy" | Out-Null
New-Item -ItemType Directory -Path ".\publish\server" | Out-Null
New-Item -ItemType Directory -Path ".\publish\desktop" | Out-Null
New-Item -ItemType Directory -Path ".\publish\desktop\lib" | Out-Null

# Build Server (self-contained win-x64)
Write-Host "[2/7] Building Server (self-contained win-x64)..." -ForegroundColor Yellow
dotnet publish ".\server\Server.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o ".\publish\server"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Server build failed!" -ForegroundColor Red
    exit 1
}

# Build Desktop (self-contained win-x64)
Write-Host "[3/7] Building Desktop (self-contained win-x64)..." -ForegroundColor Yellow
dotnet publish ".\desktop\Desktop.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o ".\publish\desktop"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Desktop build failed!" -ForegroundColor Red
    exit 1
}

# Copy Caddy
Write-Host "[4/7] Copying Caddy..." -ForegroundColor Yellow
$caddyFound = $false
$caddyCandidates = @(
    ".\installer\caddy\caddy.exe",
    "C:\Program Files\Vidikom\caddy\caddy.exe",
    "C:\Program Files\VidiComm\caddy\caddy.exe",
    "C:\Program Files\CuePilot\caddy\caddy.exe",
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
}

# Create Universal Caddyfile (listens on port 8443 across all local network interfaces without prompting)
$caddyConfig = @"
:8443 {
    tls internal
    reverse_proxy 127.0.0.1:8080
}
"@
$caddyConfig | Set-Content ".\publish\caddy\Caddyfile"

# Copy ATEM SDK & NDI Runtime DLLs
Write-Host "[5/7] Bundling ATEM SDK & NDI Runtime DLLs..." -ForegroundColor Yellow
Copy-Item ".\desktop\lib\*.dll" ".\publish\desktop\" -Force
Copy-Item ".\desktop\lib\*.dll" ".\publish\desktop\lib\" -Force

$ndiCandidates = @(
    ".\desktop\lib\Processing.NDI.Lib.x64.dll",
    "C:\Program Files\NDI\NDI 6 Tools\Runtime\Processing.NDI.Lib.x64.dll",
    "C:\Program Files\NDI\NDI 6 Runtime\v6\Processing.NDI.Lib.x64.dll",
    "C:\Program Files\NDI\NDI 6 Tools\Router\Processing.NDI.Lib.x64.dll",
    "C:\Program Files\NDI\NDI 5 Runtime\v5\Processing.NDI.Lib.x64.dll"
)
foreach ($ndiPath in $ndiCandidates) {
    if (Test-Path $ndiPath) {
        Copy-Item $ndiPath ".\desktop\lib\Processing.NDI.Lib.x64.dll" -Force -ErrorAction SilentlyContinue
        Copy-Item $ndiPath ".\publish\desktop\Processing.NDI.Lib.x64.dll" -Force
        Write-Host "Bundled NDI runtime: $ndiPath" -ForegroundColor Green
        break
    }
}

# Copy Visual C++ Redistributable Runtime DLLs so clean Windows PCs never fail loading native DLLs
Write-Host "[6/7] Bundling Visual C++ Runtime DLLs (x64)..." -ForegroundColor Yellow
$vcDlls = @(
    "vcruntime140.dll",
    "vcruntime140_1.dll",
    "msvcp140.dll",
    "msvcp140_1.dll",
    "msvcp140_2.dll",
    "concrt140.dll",
    "vcomp140.dll",
    "vccorlib140.dll"
)
foreach ($dll in $vcDlls) {
    $sysDll = Join-Path $env:SystemRoot "System32\$dll"
    if (Test-Path $sysDll) {
        Copy-Item $sysDll ".\publish\desktop\$dll" -Force
        Copy-Item $sysDll ".\publish\server\$dll" -Force
    }
}
Write-Host "Bundled MSVC C++ runtime DLLs into publish\desktop and publish\server" -ForegroundColor Green

# Bundle Microsoft Edge WebView2 Evergreen Bootstrapper for target PCs missing WebView2
Write-Host "[7/7] Ensuring WebView2 Evergreen Bootstrapper is bundled..." -ForegroundColor Yellow
$wv2Bootstrapper = ".\installer\MicrosoftEdgeWebview2Setup.exe"
if (-not (Test-Path $wv2Bootstrapper)) {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/p/?LinkId=2124703" -OutFile $wv2Bootstrapper -UseBasicParsing -TimeoutSec 20
        Write-Host "Downloaded MicrosoftEdgeWebview2Setup.exe" -ForegroundColor Green
    } catch {
        Write-Host "Note: Could not download WebView2 bootstrapper ($($_.Exception.Message))" -ForegroundColor Yellow
    }
}
if (Test-Path $wv2Bootstrapper) {
    Copy-Item $wv2Bootstrapper ".\publish\desktop\MicrosoftEdgeWebview2Setup.exe" -Force
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Output directory: .\publish\" -ForegroundColor Cyan
