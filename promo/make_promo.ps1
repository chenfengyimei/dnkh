# Promo video assembly: scene clips -> concat -> narration+BGM mix.
# Chinese text comes from promo/captions.tsv (UTF-8) to avoid WinPS ANSI issues.
param(
    [string]$Narration = "promo/audio/narration.mp3",
    [string]$Bgm = "promo/audio/bgm.mp3",
    [string]$ShotsDir = "promo/shots",
    [string]$OutFile = "promo/dnkhw_promo_1080p.mp4",
    [string]$WorkDir = "promo/out"
)

$ErrorActionPreference = "Stop"
$ffDir = (Get-ChildItem "tools/ffmpeg" -Recurse -Filter ffmpeg.exe | Select-Object -First 1).DirectoryName
$ffmpeg = Join-Path $ffDir "ffmpeg.exe"
$ffprobe = Join-Path $ffDir "ffprobe.exe"
$font = "C\:/Windows/Fonts/msyhbd.ttc"

if (-not (Test-Path $Narration)) { throw "narration missing: $Narration" }
if (-not (Test-Path $Bgm)) { throw "bgm missing: $Bgm" }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# ---- load captions (UTF-8) ----
$cap = @{}
Get-Content "promo/captions.tsv" -Encoding UTF8 | ForEach-Object {
    if ($_ -match "^(\S+)\t(.+)$") { $cap[$Matches[1]] = $Matches[2] }
}

# ---- timeline ----
$narrSec = [double](& $ffprobe -v error -show_entries format=duration -of csv=p=0 $Narration)
"narration: $narrSec s"
$leadIn = 2.5
$titleDur = 3.6
$endDur = 6.0
$buffer = 1.4
$contentSum = $narrSec + $leadIn + $buffer - $titleDur - $endDur
if ($contentSum -lt 20) { $contentSum = 20 }

# scene weights (content part)
$weights = @{ check = 16; wallpaper = 12; downloads = 13; apps = 15; startup = 9; optimize = 9; disk = 8 }
$weightTotal = ($weights.Values | Measure-Object -Sum).Sum
$sceneDur = @{}
foreach ($k in $weights.Keys) { $sceneDur[$k] = [Math]::Round($contentSum * $weights[$k] / $weightTotal, 2) }

# frame counts for timelapse scenes
$frames = @{ check = 32; wallpaper = 23; apps = 16 }
foreach ($k in @($frames.Keys)) {
    $n = (Get-ChildItem (Join-Path $ShotsDir $k) -Filter *.png).Count
    if ($n -gt 0) { $frames[$k] = $n }
}

$drawCaptionFmt = "drawtext=fontfile='{0}':text='{1}':fontcolor=white:fontsize=52:box=1:boxcolor=0x000000@0.45:boxborderw=18:x=(w-text_w)/2:y=h-text_h-84"
# official site watermark (bottom-right, on every scene)
$wmLight = "drawtext=fontfile='$font':text='www.cyhaoka.vip':fontcolor=0x37474F@0.75:fontsize=30:x=w-text_w-44:y=h-80"
$wmDark = "drawtext=fontfile='$font':text='www.cyhaoka.vip':fontcolor=0xBFD4EE@0.9:fontsize=30:x=w-text_w-44:y=h-80"

function Make-FramesClip([string]$page, [double]$dur, [string]$caption) {
    $rate = [Math]::Round($frames[$page] / $dur, 4)
    if ($rate -lt 0.8) { $rate = 0.8 }
    $inPat = (Join-Path (Join-Path $ShotsDir $page) "%03d.png")
    $out = Join-Path $WorkDir "scene_$page.mp4"
    $fadeStart = [Math]::Round($dur - 0.35, 2)
    $vf = "fps=25,scale=1920:1080,setsar=1," + ($drawCaptionFmt -f $font, $caption) + ",$wmLight,fade=t=in:st=0:d=0.35,fade=t=out:st=$fadeStart`:d=0.35"
    & $ffmpeg -y -loglevel error -framerate $rate -start_number 0 -i $inPat -vf $vf -t $dur -r 25 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p $out
    if ($LASTEXITCODE -ne 0) { throw "frames clip failed: $page" }
    return $out
}

function Make-StillClip([string]$page, [double]$dur, [string]$caption) {
    $inFile = (Join-Path (Join-Path $ShotsDir $page) "still.png")
    $out = Join-Path $WorkDir "scene_$page.mp4"
    $fadeStart = [Math]::Round($dur - 0.35, 2)
    # standard Ken Burns: upscale 2x, zoompan d=1 accumulates zoom across looped frames
    $vf = "scale=3840:2160,zoompan=z='min(zoom+0.0004,1.06)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s=1920x1080:fps=25,setsar=1," + ($drawCaptionFmt -f $font, $caption) + ",$wmLight,fade=t=in:st=0:d=0.35,fade=t=out:st=$fadeStart`:d=0.35"
    & $ffmpeg -y -loglevel error -loop 1 -framerate 25 -i $inFile -vf $vf -t $dur -r 25 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p $out
    if ($LASTEXITCODE -ne 0) { throw "still clip failed: $page" }
    return $out
}

function Make-CardClip([string]$key, [string]$subKey, [double]$dur) {
    $out = Join-Path $WorkDir "scene_$key.mp4"
    $title = $cap[$key]
    $sub = $cap[$subKey]
    $fadeStart = [Math]::Round($dur - 0.5, 2)
    $vf = "drawbox=x=860:y=520:w=200:h=6:color=0x4FA3FF@1:t=fill," +
        "drawtext=fontfile='$font':text='$title':fontcolor=white:fontsize=112:x=(w-text_w)/2:y=420," +
        "drawtext=fontfile='$font':text='$sub':fontcolor=0xBFD4EE:fontsize=44:x=(w-text_w)/2:y=590," +
        "fade=t=in:st=0:d=0.5,fade=t=out:st=$fadeStart`:d=0.5,$wmDark"
    & $ffmpeg -y -loglevel error -f lavfi -i "gradients=s=1920x1080:c0=0x0B1020:c1=0x16305F:speed=0.025:d=$dur" -vf $vf -t $dur -r 25 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p $out
    if ($LASTEXITCODE -ne 0) { throw "card clip failed: $key" }
    return $out
}

# ---- build scenes ----
$clips = @()
$clips += Make-CardClip "title" "titleSub" $titleDur
$clips += Make-FramesClip "check" $sceneDur["check"] $cap["check"]
$clips += Make-FramesClip "wallpaper" $sceneDur["wallpaper"] $cap["wallpaper"]
$clips += Make-StillClip "downloads" $sceneDur["downloads"] $cap["downloads"]
$clips += Make-FramesClip "apps" $sceneDur["apps"] $cap["apps"]
$clips += Make-StillClip "startup" $sceneDur["startup"] $cap["startup"]
$clips += Make-StillClip "optimize" $sceneDur["optimize"] $cap["optimize"]
$clips += Make-StillClip "disk" $sceneDur["disk"] $cap["disk"]
$clips += Make-CardClip "end" "endSub" $endDur

# ---- concat ----
$listFile = Join-Path $WorkDir "list.txt"
$lines = foreach ($c in $clips) { "file '$((Resolve-Path $c).Path -replace '\\', '/')'" }
Set-Content -Path $listFile -Value $lines -Encoding ASCII
$concatFile = Join-Path $WorkDir "concat.mp4"
& $ffmpeg -y -loglevel error -f concat -safe 0 -i $listFile -c copy $concatFile
if ($LASTEXITCODE -ne 0) { throw "concat failed" }

$videoSec = [double](& $ffprobe -v error -show_entries format=duration -of csv=p=0 $concatFile)
"video total: $videoSec s"

# ---- audio mix ----
$delayMs = [int]($leadIn * 1000)
$fadeSt = [Math]::Round($videoSec - 3, 2)
& $ffmpeg -y -loglevel error -i $concatFile -i $Narration -i $Bgm -filter_complex "[1:a]adelay=$delayMs|$delayMs,volume=1.0[n];[2:a]volume=0.16,afade=t=out:st=$fadeSt`:d=3[b];[n][b]amix=inputs=2:duration=longest:normalize=0[a]" -map 0:v -map "[a]" -t $videoSec -c:v copy -c:a aac -b:a 192k $OutFile
if ($LASTEXITCODE -ne 0) { throw "audio mix failed" }

$finalSec = [double](& $ffprobe -v error -show_entries format=duration -of csv=p=0 $OutFile)
$sizeMb = [Math]::Round((Get-Item $OutFile).Length / 1MB, 1)
Write-Output "DONE: $OutFile  duration=$finalSec s  size=$sizeMb MB"
