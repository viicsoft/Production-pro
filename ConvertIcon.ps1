Add-Type -AssemblyName System.Drawing
$pngPath = 'C:\Users\Hp\.gemini\antigravity\brain\3bbe714a-ec3e-4b80-aa0c-32a1f402de85\atemdirector_icon_1769726008515.png'
$icoPath = 'c:\worker\AtemDirector\installer\icon.ico'

if (Test-Path $pngPath) {
    try {
        $sourceBmp = [System.Drawing.Bitmap]::FromFile($pngPath)
        
        # We need to create a multi-frame ICO to make Inno Setup and Windows happy.
        # Since standard System.Drawing doesn't easily save multi-frame ICOs, 
        # we will create a high-quality 256x256 icon which is now the Windows standard.
        
        $targetSize = 256
        $finalBmp = New-Object System.Drawing.Bitmap($targetSize, $targetSize)
        $g = [System.Drawing.Graphics]::FromImage($finalBmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.DrawImage($sourceBmp, 0, 0, $targetSize, $targetSize)
        $g.Dispose()
        
        # Save as Icon
        $hIcon = $finalBmp.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($hIcon)
        $stream = [System.IO.File]::Create($icoPath)
        $icon.Save($stream)
        $stream.Close()
        
        $finalBmp.Dispose()
        $sourceBmp.Dispose()
        Write-Host "SUCCESS: Created professional icon at $icoPath"
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)"
    }
} else {
    Write-Host "SOURCE PNG NOT FOUND"
}
