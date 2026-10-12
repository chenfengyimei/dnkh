# Compose showcase frames for the silent promo (v2, with transitions + BGM).
# Outputs (1920x1080 PNG):
#   show_title.png / show_end.png          - fully composed cards
#   show_bg_<key>.png                      - module backgrounds (text+shadow+border+dots, NO screenshot)
#   slot_mask.png                          - grayscale rounded-rect mask for alphamerge
# The app screenshot itself is overlaid live by ffmpeg (so it can Ken Burns inside the slot).
# ALL display text comes from promo/showcase_texts.tsv (UTF-8). ASCII-only script.
param(
    [string]$StillsDir = "docs/screenshots",
    [string]$TextsFile = "promo/showcase_texts.tsv",
    [string]$OutDir = "promo/showcase"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$W = 1920; $H = 1080
$SHOT_X = 560; $SHOT_Y = 186; $SHOT_W = 1256; $SHOT_H = 707
$Rad = 18

# ---- load texts ----
$txt = @{}
$rows = @()
Get-Content $TextsFile -Encoding UTF8 | ForEach-Object {
    $p = $_ -split "`t"
    if ($p.Count -ge 2) {
        if ($p.Count -ge 4) { $rows += ,@($p[0], $p[1], $p[2], $p[3]) }
        else { $txt[$p[0]] = $p[1] }
    }
}

function New-RoundRect([int]$x, [int]$y, [int]$w, [int]$h, [int]$rad) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, ($rad * 2), ($rad * 2), 180, 90)
    $path.AddArc(($x + $w - $rad * 2), $y, ($rad * 2), ($rad * 2), 270, 90)
    $path.AddArc(($x + $w - $rad * 2), ($y + $h - $rad * 2), ($rad * 2), ($rad * 2), 0, 90)
    $path.AddArc($x, ($y + $h - $rad * 2), ($rad * 2), ($rad * 2), 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Bg([int]$index) {
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    # vertical gradient 0B1020 -> 16305F
    $rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
    $lgbr = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
        ([System.Drawing.Color]::FromArgb(255, 11, 16, 32)), `
        ([System.Drawing.Color]::FromArgb(255, 22, 48, 95)), `
        ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillRectangle($lgbr, $rect)
    $lgbr.Dispose()

    # radial glows via PathGradientBrush (proper falloff)
    function Glow([System.Drawing.Graphics]$gg, [int]$cx, [int]$cy, [int]$Rad, [int]$a, [int]$cr, [int]$cg, [int]$cb) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddEllipse(($cx - $Rad), ($cy - $Rad), ($Rad * 2), ($Rad * 2))
        $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
        $pgb.CenterColor = [System.Drawing.Color]::FromArgb($a, $cr, $cg, $cb)
        $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $cr, $cg, $cb))
        $gg.FillPath($pgb, $path)
        $pgb.Dispose(); $path.Dispose()
    }
    Glow $g 1640 60 460 60 0 122 214
    Glow $g 170 990 420 44 0 152 172
    Glow $g 960 -80 560 34 34 86 190

    # faint dot grid
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(16, 140, 170, 220))
    for ($yy = 44; $yy -le 156; $yy += 38) {
        for ($xx = 44; $xx -le 1876; $xx += 38) {
            $g.FillEllipse($dot, $xx, $yy, 2, 2)
        }
    }
    $dot.Dispose()

    if ($index -ge 0) {
        # layered shadow under the slot
        for ($k = 1; $k -le 4; $k++) {
            $alpha = 13 * (5 - $k)
            $off = $k * 3
            $sh = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha, 0, 0, 0))
            $p = New-RoundRect ($SHOT_X - $off) ($SHOT_Y - $off) $SHOT_W $SHOT_H $Rad
            $g.FillPath($sh, $p)
            $p.Dispose(); $sh.Dispose()
        }
        # accent border around the slot
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 79, 163, 255)), 2
        $bp = New-RoundRect $SHOT_X $SHOT_Y $SHOT_W $SHOT_H $Rad
        $g.DrawPath($pen, $bp)
        $bp.Dispose(); $pen.Dispose()
    }

    # progress dots (modules only)
    if ($index -ge 0) {
        for ($i = 0; $i -lt 8; $i++) {
            $cx = 100 + $i * 30
            if ($i -eq $index) {
                $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 79, 163, 255))
                $g.FillEllipse($b, $cx, 996, 12, 12)
            } else {
                $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 58, 74, 107))
                $g.FillEllipse($b, $cx, 999, 9, 9)
            }
            $b.Dispose()
        }
    }

    # watermark bottom-right
    $wmFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 17, ([System.Drawing.FontStyle]::Regular)
    $wmBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 191, 212, 238))
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Far
    $g.DrawString($txt["watermark"], $wmFont, $wmBrush, (New-Object System.Drawing.RectangleF 0, 986, 1876, 44), $sf)
    $sf.Dispose(); $wmFont.Dispose(); $wmBrush.Dispose()

    $g.Dispose()
    return $bmp
}

function Make-ModuleBg([string]$key, [string]$num, [string]$title, [string]$desc, [int]$index) {
    $bmp = New-Bg $index
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    # eyebrow: 模块 01
    $eyebrowFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 24, ([System.Drawing.FontStyle]::Bold)
    $eyebrowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 79, 163, 255))
    $g.DrawString(($txt["eyebrowLabel"] + " " + $num), $eyebrowFont, $eyebrowBrush, 96.0, 296.0)
    $eyebrowFont.Dispose(); $eyebrowBrush.Dispose()

    # accent bar
    $bar = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 79, 163, 255))
    $g.FillRectangle($bar, 96, 350, 72, 6)
    $bar.Dispose()

    # title
    $titleFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 56, ([System.Drawing.FontStyle]::Bold)
    $titleBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.DrawString($title, $titleFont, $titleBrush, 94.0, 376.0)
    $titleFont.Dispose(); $titleBrush.Dispose()

    # desc
    $descFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 26, ([System.Drawing.FontStyle]::Regular)
    $descBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 199, 214, 236))
    $descRect = New-Object System.Drawing.RectangleF 96.0, 490.0, 424.0, 280.0
    $sf = New-Object System.Drawing.StringFormat
    $sf.Trimming = [System.Drawing.StringTrimming]::EllipsisCharacter
    $g.DrawString($desc, $descFont, $descBrush, $descRect, $sf)
    $sf.Dispose(); $descFont.Dispose(); $descBrush.Dispose()

    $g.Dispose()
    $bmp.Save((Join-Path $OutDir ("show_bg_" + $key + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("bg: " + $key)
}

function Make-CardFrame([string]$outName, [string]$title, [string]$sub, [string]$capLine) {
    $bmp = New-Bg -1
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center

    $tFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 96, ([System.Drawing.FontStyle]::Bold)
    $tBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.DrawString($title, $tFont, $tBrush, (New-Object System.Drawing.RectangleF 0, 360, 1920, 150), $sf)
    $tFont.Dispose(); $tBrush.Dispose()

    $bar = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 79, 163, 255))
    $g.FillRectangle($bar, 860, 540, 200, 6)
    $bar.Dispose()

    $sFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 40, ([System.Drawing.FontStyle]::Regular)
    $sBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 191, 212, 238))
    $g.DrawString($sub, $sFont, $sBrush, (New-Object System.Drawing.RectangleF 0, 580, 1920, 70), $sf)
    $sFont.Dispose(); $sBrush.Dispose()

    $cFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 32, ([System.Drawing.FontStyle]::Regular)
    $cBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 143, 182, 232))
    $g.DrawString($capLine, $cFont, $cBrush, (New-Object System.Drawing.RectangleF 0, 860, 1920, 60), $sf)
    $cFont.Dispose(); $cBrush.Dispose()
    $sf.Dispose()

    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $outName), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("card: " + $outName)
}

# ---- slot mask (white rounded rect on black, for alphamerge) ----
$mask = New-Object System.Drawing.Bitmap $SHOT_W, $SHOT_H
$mg = [System.Drawing.Graphics]::FromImage($mask)
$mg.Clear([System.Drawing.Color]::Black)
$mg.SmoothingMode = 'AntiAlias'
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$mp = New-RoundRect 0 0 $SHOT_W $SHOT_H $Rad
$mg.FillPath($white, $mp)
$mp.Dispose(); $white.Dispose(); $mg.Dispose()
$mask.Save((Join-Path $OutDir "slot_mask.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$mask.Dispose()
Write-Output "mask: slot_mask.png"

# ---- frames ----
Make-CardFrame "show_title.png" $txt["brand"] $txt["tagline"] $txt["titleCap"]
$i = 0
foreach ($rowItem in $rows) {
    Make-ModuleBg $rowItem[0] $rowItem[1] $rowItem[2] $rowItem[3] $i
    $i++
}
Make-CardFrame "show_end.png" $txt["endTitle"] $txt["endSub"] $txt["endCap"]
Write-Output "all frames done"
