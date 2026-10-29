param([string]$OutputPath = (Join-Path $PSScriptRoot '..\src\ClipZero\Assets\clipzero.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(36, 91, 170))
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 2.2)
    $memory = [System.IO.MemoryStream]::new()
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 32.0, $size / 32.0)
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.FillEllipse($fill, 1, 1, 30, 30)
        $graphics.DrawRectangle($pen, 10, 9, 12, 16)
        $graphics.DrawLine($pen, 13, 7, 19, 7)
        $graphics.DrawLine($pen, 13, 14, 19, 14)
        $graphics.DrawLine($pen, 13, 18, 19, 18)
        $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += ,$memory.ToArray()
    } finally { $memory.Dispose(); $pen.Dispose(); $fill.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
$absolutePath = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($absolutePath)) | Out-Null
$stream = [System.IO.File]::Create($absolutePath)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output "Generated $absolutePath"
