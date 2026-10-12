# Promo v2 assembly: showcase frames + slot Ken Burns + xfade transitions + BGM.
# ASCII only on purpose (WinPS 5.1 ANSI). All display text lives in showcase PNGs.
param(
    [string]$StillsDir = "docs/screenshots",
    [string]$ShowcaseDir = "promo/showcase",
    [string]$Bgm = "promo/audio/bgm_promo.mp3",
    [string]$WorkDir = "promo/out_v2",
    [string]$OutFile = "promo/dnkhw_promo_v2_raw.mp4"
)

$ErrorActionPreference = "Stop"
$ffDir = (Get-ChildItem "tools/ffmpeg" -Recurse -Filter ffmpeg.exe | Select-Object -First 1).DirectoryName
$ffmpeg = Join-Path $ffDir "ffmpeg.exe"
$ffprobe = Join-Path $ffDir "ffprobe.exe"

if (-not (Test-Path $Bgm)) { throw "bgm missing: $Bgm" }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# ---- timeline ----
# scene durations (raw); xfade consumes 0.6s per join
$fade = 0.6
$order = @(
    @{ kind = "card";  key = "title";     dur = 4.0 },
    @{ kind = "shot";  key = "check";     dur = 6.0 },
    @{ kind = "shot";  key = "wallpaper"; dur = 5.0 },
    @{ kind = "shot";  key = "downloads"; dur = 5.0 },
    @{ kind = "shot";  key = "apps";      dur = 5.0 },
    @{ kind = "shot";  key = "startup";   dur = 4.5 },
    @{ kind = "shot";  key = "optimize";  dur = 5.0 },
    @{ kind = "shot";  key = "disk";      dur = 4.5 },
    @{ kind = "shot";  key = "website";   dur = 5.0 },
    @{ kind = "card";  key = "end";       dur = 5.0 }
)
# transitions between the 10 scenes (in order)
$trans = @("circleopen", "smoothleft", "smoothleft", "smoothleft", "smoothup", "smoothleft", "smoothleft", "smoothup", "fade")

$SHOT_X = 560; $SHOT_Y = 186; $SHOT_W = 1256; $SHOT_H = 707

function Make-CardClip([string]$key, [double]$dur) {
    $inFile = Join-Path $ShowcaseDir ("show_" + $key + ".png")
    $out = Join-Path $WorkDir ("scene_" + $key + ".mp4")
    $vf = "scale=3840:2160:flags=lanczos,zoompan=z='min(zoom+0.0002,1.045)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s=1920x1080:fps=25,setsar=1,format=yuv420p"
    & $ffmpeg -y -loglevel error -loop 1 -framerate 25 -i $inFile -vf $vf -t $dur -r 25 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -an $out
    if ($LASTEXITCODE -ne 0) { throw "card clip failed: $key" }
    return $out
}

function Make-ShotClip([string]$key, [double]$dur) {
    $bgFile = Join-Path $ShowcaseDir ("show_bg_" + $key + ".png")
    $shotFile = Join-Path $StillsDir ($key + ".png")
    $maskFile = Join-Path $ShowcaseDir "slot_mask.png"
    $out = Join-Path $WorkDir ("scene_" + $key + ".mp4")
    # Ken Burns lives INSIDE the rounded slot; panel text stays static
    $fc = "[1:v]scale=3840:2160:flags=lanczos," +
        "zoompan=z='min(zoom+0.0005,1.09)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s=2512x1414:fps=25[zs];" +
        "[zs]scale=$SHOT_W`:$SHOT_H`:flags=lanczos[zf];" +
        "[zf][2:v]alphamerge[zm];" +
        "[0:v][zm]overlay=$SHOT_X`:$SHOT_Y`:format=auto,setsar=1,format=yuv420p[v]"
    & $ffmpeg -y -loglevel error -loop 1 -framerate 25 -i $bgFile -loop 1 -framerate 25 -i $shotFile -loop 1 -framerate 25 -i $maskFile -filter_complex $fc -map "[v]" -t $dur -r 25 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -an $out
    if ($LASTEXITCODE -ne 0) { throw "shot clip failed: $key" }
    return $out
}

# ---- build scene clips ----
$clips = @()
foreach ($s in $order) {
    if ($s.kind -eq "card") { $clips += Make-CardClip $s.key $s.dur }
    else { $clips += Make-ShotClip $s.key $s.dur }
}

# ---- xfade chain ----
# offset_i = sum(d_0..d_i) - (i+1)*fade  (transition ends exactly when scene i+1 starts)
$inputArgs = @()
foreach ($c in $clips) { $inputArgs += @("-i", $c) }

$fcParts = New-Object System.Collections.Generic.List[string]
$prev = "[0:v]"
$cum = 0.0
for ($i = 0; $i -lt $trans.Count; $i++) {
    $cum += $order[$i].dur
    $offset = [Math]::Round($cum - ($i + 1) * $fade, 3)
    $label = "[x$i]"
    $fcParts.Add("$prev[$($i + 1):v]xfade=transition=$($trans[$i]):duration=$fade`:offset=$offset$label")
    $prev = $label
}
$fc = ($fcParts -join ";") + ";${prev}null[vout]"

$xfadeFile = Join-Path $WorkDir "xfade.mp4"
& $ffmpeg -y -loglevel error @inputArgs -filter_complex $fc -map "[vout]" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -an $xfadeFile
if ($LASTEXITCODE -ne 0) { throw "xfade chain failed" }

$videoSec = [double](& $ffprobe -v error -show_entries format=duration -of csv=p=0 $xfadeFile)
"xfaded: $videoSec s"

# ---- bgm mix + global fades ----
$fadeOutSt = [Math]::Round($videoSec - 3.2, 2)
$mixFc = "[0:v]fade=t=in:st=0:d=0.5,fade=t=out:st=$fadeOutSt`:d=1.2[v];" +
    "[1:a]atrim=0:$videoSec,asetpts=PTS-STARTPTS,afade=t=in:st=0:d=1.0,afade=t=out:st=$([Math]::Round($videoSec - 2.5, 2))`:d=2.5[a]"
& $ffmpeg -y -loglevel error -i $xfadeFile -i $Bgm -filter_complex $mixFc -map "[v]" -map "[a]" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -t $videoSec $OutFile
if ($LASTEXITCODE -ne 0) { throw "audio mix failed" }

$finalSec = [double](& $ffprobe -v error -show_entries format=duration -of csv=p=0 $OutFile)
$sizeMb = [Math]::Round((Get-Item $OutFile).Length / 1MB, 2)
Write-Output ("DONE: {0}  duration={1}s  size={2}MB" -f $OutFile, $finalSec, $sizeMb)
