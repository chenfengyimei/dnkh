# Silent promo assembly (no narration, no BGM): cards + Ken Burns stills + burned captions.
# Chinese text comes from promo/captions_silent.tsv (UTF-8) to avoid WinPS ANSI issues.
# ASCII only on purpose.
param(
    [string]$StillsDir = "docs/screenshots",
    [string]$CaptionFile = "promo/captions_silent.tsv",
    [string]$OutFile = "promo/dnkhw_promo_silent.mp4",
    [string]$WorkDir = "promo/out_silent"
)

$ErrorActionPreference = "Stop"
$ffDir = (Get-ChildItem "tools/ffmpeg" -Recurse -Filter ffmpeg.exe | Select-Object -First 1).DirectoryName
$ffmpeg = Join-Path $ffDir "ffmpeg.exe"
$ffprobe = Join-Path $ffDir "ffprobe.exe"
$font = "C\:/Windows/Fonts/msyhbd.ttc"

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# ---- load captions (UTF-8) ----
$cap = @{}
Get-Content $CaptionFile -Encoding UTF8 | ForEach-Object {
    if ($_ -match "^(\S+)\t(.+)$") { $cap[$Matches[1]] = $Matches[2] }
}

# ---- timeline (seconds) ----
$titleDur = 4.0
$endDur = 5.0
$sceneDur = @{
    check = 6.0; wallpaper = 5.0; downloads = 5.0; apps = 5.0;
    startup = 4.5; optimize = 5.0; disk = 4.5; website = 5.0
}

# caption: bigger than narrated version, since text is the only channel
$drawCaptionFmt = "drawtext=fontfile='{0}':text='{1}':fontcolor=white:fontsize=54:box=1:boxcolor=0x000000@0.55:boxborderw=20:x=(w-text_w)/2:y=h-text_h-84"
# official site watermark (bottom-right, on every clip)
$wmLight = "drawtext=fontfile='$font':text='www.cyhaoka.vip':fontcolor=0x37474F@0.75:fontsize=30:x=w-text_w-44:y=h-80"
$wmDark = "drawtext=fontfile='$font':text='www.cyhaoka.vip':fontcolor=0xBFD4EE@0.9:fontsize=30:x=w-text_w-44:y=h-80"

function Make-StillClip([string]$page, [double]$dur, [string]$caption) {
    $inFile = Join-Path $StillsDir ($page + ".png")
    $out = Join-Path $WorkDir ("scene_" + $page + ".mp4")
    $fadeStart = [Math]::Round($dur - 0.35, 2)
    # standard Ken Burns: upscale 2x, zoompan d=1 accumulates zoom across looped frames
    $vf = "scale=3840:2160,zoompan=z='min(zoom+0.0004,1.06)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s=1920x1080:fps=25,setsar=1," +
        ($drawCaptionFmt -f $font, $caption) + ",$wmLight,fade=t=in:st=0:d=0.35,fade=t=out:st=$fadeStart`:d=0.35"
    & $ffmpeg -y -loglevel error -loop 1 -framerate 25 -i $inFile -vf $vf -t $dur -r 25 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -an $out
    if ($LASTEXITCODE -ne 0) { throw "still clip failed: $page" }
    return $out
}

function Make-CardClip([string]$key, [string]$subKey, [string]$capKey, [double]$dur) {
    $out = Join-Path $WorkDir ("scene_" + $key + ".mp4")
    $title = $cap[$key]
    $sub = $cap[$subKey]
    $capLine = $cap[$capKey]
    $fadeStart = [Math]::Round($dur - 0.5, 2)
    $vf = "drawbox=x=860:y=520:w=200:h=6:color=0x4FA3FF@1:t=fill," +
        "drawtext=fontfile='$font':text='$title':fontcolor=white:fontsize=112:x=(w-text_w)/2:y=420," +
        "drawtext=fontfile='$font':text='$sub':fontcolor=0xBFD4EE:fontsize=44:x=(w-text_w)/2:y=590," +
        ($drawCaptionFmt -f $font, $capLine) + ",$wmDark," +
        "fade=t=in:st=0:d=0.5,fade=t=out:st=$fadeStart`:d=0.5"
    & $ffmpeg -y -loglevel error -f lavfi -i "gradients=s=1920x1080:c0=0x0B1020:c1=0x16305F:speed=0.025:d=$dur" -vf $vf -t $dur -r 25 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -an $out
    if ($LASTEXITCODE -ne 0) { throw "card clip failed: $key" }
    return $out
}

# ---- build scenes ----
$pages = @("check", "wallpaper", "downloads", "apps", "startup", "optimize", "disk", "website")
$clips = @()
$clips += Make-CardClip "title" "titleSub" "titleCap" $titleDur
foreach ($p in $pages) {
    $clips += Make-StillClip $p $sceneDur[$p] $cap[$p]
}
$clips += Make-CardClip "end" "endSub" "endCap" $endDur

# ---- concat (absolute paths, forward slashes) ----
$listFile = Join-Path $WorkDir "list.txt"
$lines = foreach ($c in $clips) { "file '$((Resolve-Path $c).Path -replace '\\', '/')'" }
Set-Content -Path $listFile -Value $lines -Encoding ASCII
$concatFile = Join-Path $WorkDir "concat.mp4"
& $ffmpeg -y -loglevel error -f concat -safe 0 -i $listFile -c copy -an $concatFile
if ($LASTEXITCODE -ne 0) { throw "concat failed" }

if ($OutFile -ne $concatFile) {
    Move-Item -Force $concatFile $OutFile
}

$finalSec = [double](& $ffprobe -v error -show_entries format=duration -of csv=p=0 $OutFile)
$streams = (& $ffprobe -v error -select_streams a -show_entries stream=index -of csv=p=0 $OutFile | Measure-Object -Line).Lines
$sizeMb = [Math]::Round((Get-Item $OutFile).Length / 1MB, 2)
Write-Output ("DONE: {0}  duration={1}s  size={2}MB  audio-streams={3}" -f $OutFile, $finalSec, $sizeMb, $streams)
