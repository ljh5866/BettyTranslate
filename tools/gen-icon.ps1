# 生成 App 图标：256x256 蓝色圆角方块 + 白色 B，封装为 ICO（PNG 条目）
Add-Type -AssemblyName System.Drawing.Common

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56
$rect = New-Object System.Drawing.Rectangle 10,10,($size-20),($size-20)
$d = $r * 2
$path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
$path.AddArc($rect.Right-$d, $rect.Y, $d, $d, 270, 90)
$path.AddArc($rect.Right-$d, $rect.Bottom-$d, $d, $d, 0, 90)
$path.AddArc($rect.X, $rect.Bottom-$d, $d, $d, 90, 90)
$path.CloseFigure()
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,15,108,189))
$g.FillPath($brush, $path)

$font = New-Object System.Drawing.Font('Segoe UI', 150, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('B', $font, [System.Drawing.Brushes]::White, $rect, $sf)

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()

$out = 'd:\Betty_Translate\src\BettyTranslate.App\Assets\AppIcon.ico'
$dir = Split-Path $out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]1)
$bw.Write([Byte]0)
$bw.Write([Byte]0)
$bw.Write([Byte]0)
$bw.Write([Byte]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]32)
$bw.Write([UInt32]$png.Length)
$bw.Write([UInt32]22)
$bw.Write($png)
$bw.Close()
$fs.Close()
$g.Dispose(); $bmp.Dispose()
Write-Host "OK: $out"
