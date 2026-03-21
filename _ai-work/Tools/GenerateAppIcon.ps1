Add-Type -AssemblyName System.Drawing

function New-DiamondBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $ghBlue  = [System.Drawing.Color]::FromArgb(255, 0x58, 0xa6, 0xff)
    $edgeCol = [System.Drawing.Color]::FromArgb(200, 0x1f, 0x6f, 0xed)
    $brush   = New-Object System.Drawing.SolidBrush($ghBlue)

    $m    = [int]([Math]::Round($size * 0.06))
    $half = [int]($size / 2)
    $pts  = @(
        [System.Drawing.Point]::new($half,       $m),
        [System.Drawing.Point]::new($size - $m,  $half),
        [System.Drawing.Point]::new($half,       $size - $m),
        [System.Drawing.Point]::new($m,          $half)
    )
    $g.FillPolygon($brush, $pts)

    $penWidth = [Math]::Max(1.5, $size * 0.015)
    $pen = New-Object System.Drawing.Pen($edgeCol, $penWidth)
    $g.DrawPolygon($pen, $pts)

    $g.Dispose(); $pen.Dispose(); $brush.Dispose()
    return $bmp
}

function ConvertTo-IcoBytes([int[]]$sizes) {
    $entries  = @()
    $pngChunks = @()

    foreach ($sz in $sizes) {
        $bmp = New-DiamondBitmap $sz
        $ms  = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngChunks += ,$ms.ToArray()
        $ms.Dispose()
    }

    # ICONDIR = 6 bytes, ICONDIRENTRY = 16 bytes each
    $headerSize = 6 + 16 * $sizes.Count
    $offset = $headerSize

    $ico = New-Object System.IO.MemoryStream
    $w   = New-Object System.IO.BinaryWriter($ico)

    # ICONDIR
    $w.Write([uint16]0)               # reserved
    $w.Write([uint16]1)               # type: icon
    $w.Write([uint16]$sizes.Count)    # count

    # ICONDIRENTRY per size
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz  = $sizes[$i]
        $dim = if ($sz -ge 256) { 0 } else { [byte]$sz }   # 0 means 256
        $w.Write([byte]$dim)          # width
        $w.Write([byte]$dim)          # height
        $w.Write([byte]0)             # colorCount
        $w.Write([byte]0)             # reserved
        $w.Write([uint16]1)           # planes
        $w.Write([uint16]32)          # bitCount
        $w.Write([uint32]$pngChunks[$i].Length)
        $w.Write([uint32]$offset)
        $offset += $pngChunks[$i].Length
    }

    foreach ($chunk in $pngChunks) { $w.Write($chunk) }
    $w.Flush()
    return $ico.ToArray()
}

$outPath = Join-Path $PSScriptRoot "..\Assets\app.ico"
$bytes   = ConvertTo-IcoBytes @(16, 32, 48, 256)
[System.IO.File]::WriteAllBytes($outPath, $bytes)
Write-Host "Generated: $outPath  ($($bytes.Length) bytes)"
