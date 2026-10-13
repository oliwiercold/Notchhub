using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Shell.Internal;
using NotchHub.Platform.Shell.Internal.Interop;

namespace NotchHub.Platform.Shell.Foreground;

/// <summary>
/// Tracks the foreground window with WinEvent hooks (no polling): remembers the last external window for paste,
/// and detects fullscreen apps on the notch's monitor (window covering the monitor, or the shell reporting
/// D3D-fullscreen / presentation mode).
/// </summary>
public sealed class ForegroundService : IForegroundService
{
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const ushort VK_CONTROL = 0x11, VK_V = 0x56, VK_SHIFT = 0x10, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private static ForegroundService? _instance;

    private readonly object _gate = new();
    private StaThread.Lease? _lease;
    private IDisposable? _messageHook;
    private nint _foregroundHook;
    private nint _locationHook;
    private uint _locationPid;
    private nint _ownWindow;
    private nint _foreground;
    private nint _lastExternal;
    private string? _lastExternalName;
    private volatile bool _fullscreen;
    private bool _evaluatePending;
    private bool _disposed;

    /// <summary>A fullscreen app is in the foreground on the notch's monitor.</summary>
    public bool IsFullscreenAppActive => _fullscreen;

    /// <summary>Raised (on the shell STA thread) when <see cref="IsFullscreenAppActive"/> changes.</summary>
    public event EventHandler? FullscreenChanged;

    /// <summary>Friendly name of the last foreground app that is not NotchHub.</summary>
    public string? LastExternalAppName
    {
        get { lock (_gate) return _lastExternalName; }
    }

    /// <summary>Installs the WinEvent hooks on the shell STA thread.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        StaThread.Lease lease;
        try
        {
            lease = await StaThread.AcquireAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Foreground: STA thread unavailable, fullscreen detection disabled", ex);
            return;
        }

        lock (_gate)
        {
            if (_disposed || _lease is not null)
            {
                lease.Dispose();
                return;
            }
            _lease = lease;
        }
        _instance = this;
        var sta = lease.Thread;
        _messageHook = sta.AddMessageHandler(OnMessage);
        await sta.InvokeAsync(() =>
        {
            _foregroundHook = Hook(Win32.EVENT_SYSTEM_FOREGROUND, 0);

            if (_foregroundHook == 0) Log.Warn($"Foreground: SetWinEventHook failed ({Win32.LastError()})");
            OnForegroundChanged(Win32.GetForegroundWindow());
        }).ConfigureAwait(false);
    }

    /// <summary>Tells the tracker which window is ours.</summary>
    public void SetOwnWindow(IntPtr hwnd)
    {
        lock (_gate) _ownWindow = hwnd;
        ScheduleEvaluate();
    }

    /// <summary>Re-activates the last external foreground window.</summary>
    public bool RestorePreviousForeground()
    {
        nint target;
        lock (_gate) target = _lastExternal;
        if (target == 0 || !Win32.IsWindow(target)) return false;

        bool Activate()
        {
            try
            {
                if (Win32.IsIconic(target)) Win32.ShowWindow(target, Win32.SW_RESTORE);
                var current = Win32.GetForegroundWindow();
                if (current == target) return true;
                var thisThread = Win32.GetCurrentThreadId();
                var fgThread = current == 0 ? 0 : Win32.GetWindowThreadProcessId(current, out _);
                var attached = fgThread != 0 && fgThread != thisThread && Win32.AttachThreadInput(thisThread, fgThread, true);
                try
                {
                    Win32.BringWindowToTop(target);
                    return Win32.SetForegroundWindow(target);
                }
                finally
                {
                    if (attached) Win32.AttachThreadInput(thisThread, fgThread, false);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Foreground: restoring the previous window failed", ex);
                return false;
            }
        }

        var sta = _lease?.Thread;
        return sta is not null ? sta.Invoke(Activate, false, TimeSpan.FromSeconds(1)) : Activate();
    }

    /// <summary>Sends Ctrl+V to the foreground window via SendInput (releasing held modifiers first).</summary>
    public unsafe void SendPasteKeystroke()
    {
        var inputs = stackalloc INPUT[10];
        var n = 0;
        foreach (var vk in new[] { VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN })
        {
            if (Win32.GetAsyncKeyState(vk) < 0) inputs[n++] = Key(vk, up: true);
        }
        inputs[n++] = Key(VK_CONTROL, up: false);
        inputs[n++] = Key(VK_V, up: false);
        inputs[n++] = Key(VK_V, up: true);
        inputs[n++] = Key(VK_CONTROL, up: true);
        var sent = Win32.SendInput((uint)n, inputs, sizeof(INPUT));
        if (sent != n) Log.Warn($"Foreground: SendInput sent {sent}/{n} events ({Win32.LastError()})");
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)Win32.MapVirtualKey(vk, Win32.MAPVK_VK_TO_VSC),
                dwFlags = (up ? Win32.KEYEVENTF_KEYUP : 0) | (vk is VK_LWIN or VK_RWIN ? Win32.KEYEVENTF_EXTENDEDKEY : 0),
            },
        },
    };

    private static unsafe nint Hook(uint ev, uint pid) =>
        Win32.SetWinEventHook(ev, ev, 0, &OnWinEvent, pid, 0, Win32.WINEVENT_OUTOFCONTEXT);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        var self = _instance;
        if (self is null) return;
        try
        {
            if (ev == Win32.EVENT_SYSTEM_FOREGROUND)
            {
                self.OnForegroundChanged(hwnd);
            }
            else if (ev == EVENT_OBJECT_LOCATIONCHANGE && idObject == Win32.OBJID_WINDOW && idChild == Win32.CHILDID_SELF
                     && hwnd == self._foreground)
            {
                self.ScheduleEvaluate();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Foreground: WinEvent handler failed", ex);
        }
    }

    /// <summary>Runs on the STA thread.</summary>
    private void OnForegroundChanged(nint hwnd)
    {
        if (_disposed) return;
        _foreground = hwnd;
        var pid = hwnd == 0 ? 0 : Win32.GetWindowProcessId(hwnd);
        var own = pid == (uint)ProcessInfo.CurrentProcessId;
        if (hwnd != 0 && !own && !IsShellSurface(hwnd))
        {
            var identity = ProcessInfo.DescribeWindow(hwnd);
            lock (_gate)
            {
                _lastExternal = hwnd;
                _lastExternalName = identity?.FriendlyName ?? _lastExternalName;
            }
        }

        // Follow position changes of the foreground window only (F11 in a browser does not change the foreground).
        if (pid != _locationPid)
        {
            if (_locationHook != 0) Win32.UnhookWinEvent(_locationHook);
            _locationHook = 0;
            _locationPid = pid;
            if (pid != 0 && !own)
            {
                _locationHook = Hook(EVENT_OBJECT_LOCATIONCHANGE, pid);

            }
        }
        Evaluate();
    }

    private static bool IsShellSurface(nint hwnd)
    {
        var cls = Win32.GetClassName(hwnd);
        return cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "NotifyIconOverflowWindow"
            or "TopLevelWindowForOverflowXamlIsland" or "XamlExplorerHostIslandWindow";
    }

    private void ScheduleEvaluate()
    {
        var sta = _lease?.Thread;
        if (sta is null || !sta.IsAlive) return;
        lock (_gate)
        {
            if (_evaluatePending) return;
            _evaluatePending = true;
        }
        // Coalesce bursts of location changes (window drags) into one check.
        sta.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            lock (_gate) _evaluatePending = false;
            Evaluate();
        });
    }

    private void Evaluate()
    {
        if (_disposed) return;
        var value = ComputeFullscreen(_foreground);
        if (value == _fullscreen) return;
        _fullscreen = value;
        Log.Info($"Foreground: fullscreen app {(value ? "active" : "gone")}");
        try { FullscreenChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Error("Foreground: FullscreenChanged handler failed", ex); }
    }

    private bool ComputeFullscreen(nint hwnd)
    {
        if (hwnd == 0 || !Win32.IsWindow(hwnd) || !Win32.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd)) return false;
        if (Win32.GetWindowProcessId(hwnd) == (uint)ProcessInfo.CurrentProcessId) return false;
        var cls = Win32.GetClassName(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        using var dpi = Win32.PerMonitorDpiScope.Enter();
        nint own;
        lock (_gate) own = _ownWindow;
        var notchMonitor = own != 0
            ? Win32.MonitorFromWindow(own, Win32.MONITOR_DEFAULTTOPRIMARY)
            : Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTOPRIMARY);
        if (Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONULL) != notchMonitor) return false;

        if (Win32.SHQueryUserNotificationState(out var state) == 0
            && state is Win32.QUNS_RUNNING_D3D_FULL_SCREEN or Win32.QUNS_PRESENTATION_MODE)
        {
            return true;
        }

        if (!Win32.TryGetMonitorRect(notchMonitor, out var monitorRect) || !Win32.GetWindowRect(hwnd, out var rect)) return false;
        if (!rect.Contains(monitorRect)) return false;
        // Maximized captioned windows overhang the monitor by their borders; those are not fullscreen.
        var style = (uint)(long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
        return !((style & Win32.WS_CAPTION) == Win32.WS_CAPTION && Win32.IsZoomed(hwnd));
    }

    private bool OnMessage(nint hwnd, int msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_DISPLAYCHANGE) ScheduleEvaluate();
        return false;
    }

    /// <summary>Removes the hooks and releases the STA thread.</summary>
    public void Dispose()
    {
        StaThread.Lease? lease;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            lease = _lease;
            _lease = null;
        }
        if (ReferenceEquals(_instance, this)) _instance = null;
        _messageHook?.Dispose();
        if (lease is null) return;
        lease.Thread.Invoke(() =>
        {
            if (_foregroundHook != 0) Win32.UnhookWinEvent(_foregroundHook);
            if (_locationHook != 0) Win32.UnhookWinEvent(_locationHook);
            _foregroundHook = _locationHook = 0;
            return true;
        }, false, TimeSpan.FromSeconds(1));
        lease.Dispose();
    }
}
