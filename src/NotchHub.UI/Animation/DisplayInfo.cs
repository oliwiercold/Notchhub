using System.Runtime.InteropServices;
using System.Windows.Media;

namespace NotchHub.UI.Animation;

/// <summary>Display refresh-rate queries (EnumDisplaySettings, DWM fallback) and a debug frame counter.</summary>
public static class DisplayInfo
{
    /// <summary>Refresh rate (Hz) of the monitor showing <paramref name="hwnd"/>; 60 when unknown.</summary>
    public static double RefreshHz(IntPtr hwnd)
    {
        try
        {
            var mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info))
            {
                var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettings(info.szDevice, -1 /* ENUM_CURRENT_SETTINGS */, ref mode) && mode.dmDisplayFrequency > 1)
                    return mode.dmDisplayFrequency;
            }
            var timing = new DWM_TIMING_INFO { cbSize = Marshal.SizeOf<DWM_TIMING_INFO>() };
            if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing) == 0 && timing.rateRefresh.uiDenominator != 0)
            {
                var hz = (double)timing.rateRefresh.uiNumerator / timing.rateRefresh.uiDenominator;
                if (hz > 1) return hz;
            }
        }
        catch (Exception ex) { NotchHub.Core.Log.Debug("RefreshHz failed: " + ex.Message); }
        return 60;
    }

    /// <summary>Highest refresh rate across all monitors (Hz).</summary>
    public static double MaxRefreshHz()
    {
        double max = 0;
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, IntPtr _, IntPtr _) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(h, ref info))
                {
                    var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                    if (EnumDisplaySettings(info.szDevice, -1, ref mode) && mode.dmDisplayFrequency > 1)
                        max = Math.Max(max, mode.dmDisplayFrequency);
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { NotchHub.Core.Log.Debug("MaxRefreshHz failed: " + ex.Message); }
        return max > 1 ? max : 60;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE mode);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("dwmapi.dll")] private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DWM_TIMING_INFO info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)] private struct UNSIGNED_RATIO { public uint uiNumerator, uiDenominator; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DWM_TIMING_INFO
    {
        public int cbSize;
        public UNSIGNED_RATIO rateRefresh;
        public ulong qpcRefreshPeriod;
        public UNSIGNED_RATIO rateCompose;
        public ulong qpcVBlank, cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose, cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame, cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong cFramesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed, qpcFrameDisplayed, cRefreshFrameDisplayed, cFrameComplete, qpcFrameComplete;
        public ulong cFramePending, qpcFramePending, cFramesDisplayed, cFramesComplete, cFramesPending, cFramesAvailable;
        public ulong cFramesDropped, cFramesMissed, cRefreshNextDisplayed, cRefreshNextPresented, cRefreshesDisplayed;
        public ulong cRefreshesPresented, cRefreshStarted, cPixelsReceived, cPixelsDrawn, cBuffersEmpty;
    }
}

/// <summary>
/// Debug frame counter: counts presented frames (CompositionTarget.Rendering, deduped by RenderingTime) between
/// <see cref="Begin"/> and <see cref="End"/>; reports fps, mean / worst frame time and dropped frames (dt &gt; 1.5 × period).
/// Costs nothing unless started.
/// </summary>
public sealed class FrameMonitor
{
    private TimeSpan _last;
    private int _frames, _dropped;
    private double _sum, _worst, _period;
    private long _startTicks;
    private bool _on;

    public void Begin(double refreshHz)
    {
        if (_on) return;
        _on = true;
        _period = 1 / Math.Max(1, refreshHz);
        _frames = _dropped = 0;
        _sum = _worst = 0;
        _last = TimeSpan.Zero;
        _startTicks = Environment.TickCount64;
        CompositionTarget.Rendering += OnRendering;
    }

    public string End()
    {
        if (!_on) return "";
        _on = false;
        CompositionTarget.Rendering -= OnRendering;
        var seconds = Math.Max(1e-3, (Environment.TickCount64 - _startTicks) / 1000.0);
        var mean = _frames > 1 ? _sum / (_frames - 1) : 0;
        return FormattableString.Invariant(
            $"frames={_frames} over {seconds:0.000}s, fps={(_frames - 1) / Math.Max(1e-3, _sum):0.0}, mean dt={mean * 1000:0.00}ms, worst dt={_worst * 1000:0.00}ms, dropped={_dropped}");
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs re || re.RenderingTime == _last) return;
        if (_last != TimeSpan.Zero)
        {
            var dt = (re.RenderingTime - _last).TotalSeconds;
            _sum += dt;
            _worst = Math.Max(_worst, dt);
            if (dt > 1.5 * _period) _dropped++;
        }
        _last = re.RenderingTime;
        _frames++;
    }
}
