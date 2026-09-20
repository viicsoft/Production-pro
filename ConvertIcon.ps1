param(
    [string]$ImagePath,
    [string]$IconPath
)
Add-Type -AssemblyName System.Drawing
if (Test-Path $ImagePath) {
    try {
        $sourceBmp = [System.Drawing.Bitmap]::FromFile($ImagePath)
        $targetSize = 256
        $finalBmp = New-Object System.Drawing.Bitmap($targetSize, $targetSize)
        $g = [System.Drawing.Graphics]::FromImage($finalBmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.DrawImage($sourceBmp, 0, 0, $targetSize, $targetSize)
        $g.Dispose()
        $hIcon = $finalBmp.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($hIcon)
        $stream = [System.IO.File]::Create($IconPath)
        $icon.Save($stream)
        $stream.Close()
        $finalBmp.Dispose()
        $sourceBmp.Dispose()
        Write-Host "SUCCESS"
    } catch {
        Write-Host "ERROR: $$($_.Exception.Message)"
    }
} else {
    Write-Host "SOURCE PNG NOT FOUND"
}
