using System.Runtime.InteropServices;
using System.Windows.Interop;
using NotchHub.App.Interop;
using NotchHub.Core;

namespace NotchHub.App.Shell;

/// <summary>
/// Reserves a strip at the top of the notch monitor via a Windows AppBar (SHAppBarMessage) so maximized windows end
/// below the collapsed notch. Uses its own hidden tool window; the strip stays transparent (nothing is drawn).
/// </summary>
public sealed class AppBarReservation : IDisposable
{
    private static readonly uint CallbackMessage = NativeMethods.RegisterWindowMessage("NotchHub.AppBar");
    private HwndSource? _source;
    private volatile bool _registered;
    private double _heightDip;
    private int _monitorIndex = -1;

    /// <summary>ABN_FULLSCREENAPP: a fullscreen app opened (true) or closed (false).</summary>
    public event EventHandler<bool>? FullscreenChanged;

    public bool IsRegistered => _registered;

    /// <summary>Registers / repositions / removes the AppBar. UI thread.</summary>
    public void Update(bool enabled, double heightDip, int monitorIndex)
    {
        if (!enabled)
        {
            Remove();
            return;
        }
        _heightDip = Math.Max(1, heightDip);
        _monitorIndex = monitorIndex;
        EnsureWindow();
        if (!_registered)
        {
            var data = NewData();
            data.uCallbackMessage = CallbackMessage;
            _registered = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data) != UIntPtr.Zero;
            if (!_registered) { Log.Warn("AppBar registration failed"); return; }
            Log.Info("AppBar registered");
        }
        SetPosition();
    }

    private NativeMethods.APPBARDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        hWnd = _source?.Handle ?? IntPtr.Zero,
        uEdge = NativeMethods.ABE_TOP,
    };

    private void EnsureWindow()
    {
        if (_source is not null) return;
        var p = new HwndSourceParameters("NotchHubAppBar")
        {
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = (int)(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE),
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    private void SetPosition()
    {
        if (!_registered) return;
        var mon = NativeMethods.GetNotchMonitor(_monitorIndex);
        if (mon is null) return;
        int h = (int)Math.Ceiling(_heightDip * mon.Scale);
        var data = NewData();
        data.rc = new NativeMethods.RECT { Left = mon.Bounds.Left, Top = mon.Bounds.Top, Right = mon.Bounds.Right, Bottom = mon.Bounds.Top + h };
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);
        data.rc.Bottom = data.rc.Top + h;
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);
        Log.Debug($"AppBar at {data.rc.Left},{data.rc.Top} {data.rc.Width}x{data.rc.Height}");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage && _registered)
        {
            switch (wParam.ToInt32())
            {
                case NativeMethods.ABN_POSCHANGED:
                    SetPosition();
                    break;
                case NativeMethods.ABN_FULLSCREENAPP:
                    FullscreenChanged?.Invoke(this, lParam != IntPtr.Zero);
                    break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>ABM_REMOVE. Safe to call from any thread, repeatedly (exit / crash handlers).</summary>
    public void Remove()
    {
        if (!_registered || _source is null) return;
        _registered = false;
        var data = NewData();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
        Log.Info("AppBar removed");
    }

    public void Dispose()
    {
        Remove();
        if (_source is { } s && s.Dispatcher.CheckAccess()) s.Dispose();
        _source = null;
    }
}
