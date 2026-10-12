# Promo shot capture: launches the app per page (--page), resizes the window to
# 1920x1080 physical, and captures PrintWindow stills / frame sequences.
# ASCII only on purpose: WinPS 5.1 reads .ps1 as ANSI when no BOM present.
param(
    [string]$ExePath = "src/Dnkhw/bin/Debug/net10.0-windows/Dnkhw.exe",
    [string]$OutDir = "promo/shots"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CapW {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RC { public int L; public int T; public int R; public int B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
}
"@
[CapW]::SetProcessDPIAware() | Out-Null

function Wait-MainHandle([System.Diagnostics.Process]$proc) {
    for ($i = 0; $i -lt 60; $i++) {
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { return $proc.MainWindowHandle }
        Start-Sleep -Milliseconds 250
    }
    throw "window handle not found"
}

function Capture-Window([IntPtr]$h, [string]$file) {
    $r = New-Object CapW+RC
    [CapW]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.R - $r.L
    $hh = $r.B - $r.T
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [CapW]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# page, mode(frames|still), durationSec, intervalMs
$plan = @(
    @{ page = "check";     mode = "frames"; duration = 26; interval = 800 },
    @{ page = "wallpaper"; mode = "frames"; duration = 14; interval = 600 },
    @{ page = "apps";      mode = "frames"; duration = 9;  interval = 550 },
    @{ page = "downloads"; mode = "still";  duration = 30; interval = 0 },
    @{ page = "startup";   mode = "still";  duration = 5;  interval = 0 },
    @{ page = "optimize";  mode = "still";  duration = 5;  interval = 0 },
    @{ page = "disk";      mode = "still";  duration = 14; interval = 0 },
    @{ page = "about";     mode = "still";  duration = 3;  interval = 0 }
)

foreach ($item in $plan) {
    $page = $item.page
    $dir = Join-Path $OutDir $page
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    $proc = Start-Process -FilePath $ExePath -ArgumentList "--page", $page -PassThru
    try {
        $h = Wait-MainHandle $proc
        Start-Sleep -Milliseconds 900
        [CapW]::MoveWindow($h, 0, 0, 1920, 1080, $true) | Out-Null
        Start-Sleep -Milliseconds 1500

        if ($item.mode -eq "frames") {
            $count = [int]($item.duration * 1000 / $item.interval)
            for ($i = 0; $i -lt $count; $i++) {
                Capture-Window $h (Join-Path $dir ("{0:D3}.png" -f $i))
                Start-Sleep -Milliseconds $item.interval
            }
            Write-Output ("{0}: {1} frames" -f $page, $count)
        }
        else {
            Start-Sleep -Seconds $item.duration
            Capture-Window $h (Join-Path $dir "still.png")
            Write-Output ("{0}: still" -f $page)
        }
    }
    finally {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    }
}

Write-Output "capture done"
