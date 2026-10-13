using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Shell.Internal;
using NotchHub.Platform.Shell.Internal.Interop;

namespace NotchHub.Platform.Shell.Hotkeys;

/// <summary>Global hotkeys registered with RegisterHotKey on the shell STA message window.</summary>
public sealed class HotkeyService : IHotkeyService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Registration> _byId = new(StringComparer.Ordinal);
    private StaThread.Lease? _lease;
    private IDisposable? _hook;
    private int _nextAtom = 0x100;
    private bool _disposed;

    private sealed record Registration(int Atom, Action Callback);

    /// <summary>Starts the STA thread that receives WM_HOTKEY.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => { EnsureThread(); }, cancellationToken);

    /// <summary>Registers or replaces the hotkey with <paramref name="id"/>.</summary>
    public bool Register(string id, string gesture, Action callback)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(callback);
        if (!GestureParser.TryParse(gesture, out var mods, out var vk)) return false;
        var sta = EnsureThread();
        if (sta is null) return false;

        Unregister(id);
        int atom;
        lock (_gate) atom = _nextAtom++;
        var ok = sta.Invoke(() =>
        {
            if (Win32.RegisterHotKey(sta.MessageWindow, atom, mods | Win32.MOD_NOREPEAT, vk)) return true;
            var error = Win32.LastError();
            Log.Warn(error == Win32.ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"Hotkeys: '{gesture}' ({id}) is already taken by another app"
                : $"Hotkeys: RegisterHotKey failed for '{gesture}' ({id}), error {error}");
            return false;
        }, false, TimeSpan.FromSeconds(2));
        if (!ok) return false;

        lock (_gate) _byId[id] = new Registration(atom, callback);
        return true;
    }

    /// <summary>Removes the hotkey with <paramref name="id"/> (no-op when unknown).</summary>
    public void Unregister(string id)
    {
        Registration? reg;
        StaThread? sta;
        lock (_gate)
        {
            if (!_byId.Remove(id, out reg)) return;
            sta = _lease?.Thread;
        }
        sta?.Invoke(() => Win32.UnregisterHotKey(sta.MessageWindow, reg.Atom), false, TimeSpan.FromSeconds(2));
    }

    /// <summary>True if the gesture string can be parsed.</summary>
    public bool IsValidGesture(string gesture) => GestureParser.TryParse(gesture, out _, out _);

    private StaThread? EnsureThread()
    {
        lock (_gate)
        {
            if (_disposed) return null;
            if (_lease is not null) return _lease.Thread;
            try
            {
                _lease = StaThread.Acquire();
                _hook = _lease.Thread.AddMessageHandler(OnMessage);
                return _lease.Thread;
            }
            catch (Exception ex)
            {
                Log.Error("Hotkeys: STA thread unavailable", ex);
                return null;
            }
        }
    }

    private bool OnMessage(nint hwnd, int msg, nint wParam, nint lParam)
    {
        if (msg != Win32.WM_HOTKEY) return false;
        var atom = (int)wParam;
        Action? callback = null;
        lock (_gate)
        {
            foreach (var reg in _byId.Values)
            {
                if (reg.Atom == atom) { callback = reg.Callback; break; }
            }
        }
        if (callback is null) return false;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { callback(); }
            catch (Exception ex) { Log.Error("Hotkeys: callback failed", ex); }
        });
        return true;
    }

    /// <summary>Unregisters every hotkey and releases the STA thread.</summary>
    public void Dispose()
    {
        Registration[] regs;
        StaThread.Lease? lease;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            regs = [.. _byId.Values];
            _byId.Clear();
            lease = _lease;
            _lease = null;
        }
        if (lease is null) return;
        var sta = lease.Thread;
        sta.Invoke(() =>
        {
            foreach (var r in regs) Win32.UnregisterHotKey(sta.MessageWindow, r.Atom);
            return true;
        }, false, TimeSpan.FromSeconds(2));
        _hook?.Dispose();
        lease.Dispose();
    }
}
