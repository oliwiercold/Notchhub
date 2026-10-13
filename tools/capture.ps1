# Captures a region of the primary screen (layered windows included) to a PNG.
# Usage: pwsh tools/capture.ps1 -Out shot.png [-X 0 -Y 0 -W 1200 -H 700]   (defaults: top-center 1200x700)
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$X = -1, [int]$Y = 0, [int]$W = 1200, [int]$H = 700
)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class DpiHelper {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
  [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
}
"@
[DpiHelper]::SetProcessDPIAware() | Out-Null
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($X -lt 0) { $X = [int](($screen.Width - $W) / 2) }
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdcDst = $g.GetHdc(); $hdcSrc = [DpiHelper]::GetDC([IntPtr]::Zero)
# SRCCOPY | CAPTUREBLT so layered (transparent) windows are included
[DpiHelper]::BitBlt($hdcDst, 0, 0, $W, $H, $hdcSrc, $X, $Y, 0x00CC0020 -bor 0x40000000) | Out-Null
[DpiHelper]::ReleaseDC([IntPtr]::Zero, $hdcSrc) | Out-Null; $g.ReleaseHdc($hdcDst)
$dir = Split-Path -Parent $Out
if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "saved $Out ($W x $H at $X,$Y; screen $($screen.Width)x$($screen.Height))"
