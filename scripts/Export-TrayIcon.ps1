param([string]$SourcePath, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SourcePath) { $SourcePath = Join-Path $projectRoot 'assets/tray-icon.png' }
if (-not $OutputPath) { $OutputPath = Join-Path $projectRoot 'assets/tray.ico' }
Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Bitmap]::new($SourcePath)
$sizes = @(16,20,24,32,48,64)
$images = [System.Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $ratio = [Math]::Min(($size-2)/$source.Width,($size-2)/$source.Height)
            $width = [Math]::Max(1,[int]($source.Width*$ratio))
            $height = [Math]::Max(1,[int]($source.Height*$ratio))
            $rectangle = [System.Drawing.Rectangle]::new([int](($size-$width)/2),[int](($size-$height)/2),$width,$height)
            $graphics.DrawImage($source,$rectangle)
            $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
        } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
    }
    $file = [System.IO.File]::Create($OutputPath)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
        $offset = 6 + 16*$sizes.Count
        for ($index=0; $index -lt $sizes.Count; $index++) {
            $writer.Write([byte]$sizes[$index]); $writer.Write([byte]$sizes[$index])
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([UInt16]1); $writer.Write([UInt16]32)
            $writer.Write([UInt32]$images[$index].Length); $writer.Write([UInt32]$offset)
            $offset += $images[$index].Length
        }
        foreach ($image in $images) { $writer.Write($image,0,$image.Length) }
    } finally { $writer.Dispose(); $file.Dispose() }
} finally { $source.Dispose() }
Write-Output $OutputPath
