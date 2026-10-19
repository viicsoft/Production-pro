Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$unalignedApk = "VidikomCrew-unaligned.apk"
$alignedApk = "VidikomCrew-aligned.apk"
$finalApk = "VidikomCrew.apk"
$srcApk = "android/app/build/outputs/apk/debug/app-debug.apk"
$newBundle = (Resolve-Path "android/app/src/main/assets/index.android.bundle").Path

Copy-Item $srcApk $unalignedApk -Force

$zip = [System.IO.Compression.ZipFile]::Open($unalignedApk, [System.IO.Compression.ZipArchiveMode]::Update)

# 1. Delete signature files from META-INF
$toDelete = @($zip.Entries | Where-Object { $_.FullName -like "META-INF/*" -and ($_.FullName -like "*.SF" -or $_.FullName -like "*.RSA" -or $_.FullName -like "*.MF" -or $_.FullName -like "*SIG*") })
foreach ($entry in $toDelete) {
    Write-Host "Removing signature file: $($entry.FullName)"
    $entry.Delete()
}

# 2. Delete old bundle
$bundleEntry = $zip.GetEntry("assets/index.android.bundle")
if ($bundleEntry) {
    Write-Host "Removing old bundle: $($bundleEntry.Length) bytes"
    $bundleEntry.Delete()
}

# 3. Add updated bundle
Write-Host "Adding new bundle from $newBundle"
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $newBundle, "assets/index.android.bundle", [System.IO.Compression.CompressionLevel]::Optimal)

$zip.Dispose()
Write-Host "APK archive updated successfully."

# 4. Zipalign
$zipalign = "C:\Users\USER\AppData\Local\Android\Sdk\build-tools\34.0.0\zipalign.exe"
if (Test-Path $alignedApk) { Remove-Item $alignedApk -Force }
Write-Host "Running zipalign..."
& $zipalign -p -f 4 $unalignedApk $alignedApk
if ($LASTEXITCODE -ne 0) {
    Write-Error "Zipalign failed with exit code $LASTEXITCODE"
    exit 1
}

# 5. Sign with apksigner
$apksigner = "C:\Users\USER\AppData\Local\Android\Sdk\build-tools\34.0.0\apksigner.bat"
$keystore = "android/app/debug.keystore"
if (Test-Path $finalApk) { Remove-Item $finalApk -Force }
Write-Host "Signing APK..."
& $apksigner sign --ks $keystore --ks-pass pass:android --ks-key-alias androiddebugkey --key-pass pass:android --out $finalApk $alignedApk
if ($LASTEXITCODE -ne 0) {
    Write-Error "Apksigner failed with exit code $LASTEXITCODE"
    exit 1
}

# 6. Verify signature
Write-Host "Verifying signature..."
& $apksigner verify --verbose $finalApk

Write-Host "SUCCESS: $finalApk is ready for installation!"
