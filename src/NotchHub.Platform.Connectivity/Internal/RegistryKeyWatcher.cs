using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using NotchHub.Core;

namespace NotchHub.Platform.Connectivity.Internal;

/// <summary>
/// Watches one HKCU key for value changes with <c>RegNotifyChangeKeyValue</c> (thread-agnostic registration, waited on
/// by the thread pool — no dedicated thread). Survives the key being deleted / recreated: when the key is missing it
/// is re-opened periodically. <paramref name="onChanged"/> runs on a thread-pool thread; exceptions are logged.
/// </summary>
internal sealed partial class RegistryKeyWatcher : IDisposable
{
    private static readonly TimeSpan ReopenInterval = TimeSpan.FromSeconds(30);

    private const uint RegNotifyChangeName = 0x00000001;
    private const uint RegNotifyChangeLastSet = 0x00000004;
    private const uint RegNotifyThreadAgnostic = 0x10000000;

    private readonly string _subKeyPath;
    private readonly Action _onChanged;
    private readonly object _gate = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Timer _reopenTimer;
    private RegisteredWaitHandle? _wait;
    private RegistryKey? _key;
    private bool _disposed;

    public RegistryKeyWatcher(string subKeyPath, Action onChanged)
    {
        _subKeyPath = subKeyPath;
        _onChanged = onChanged;
        _reopenTimer = new Timer(_ => OnReopenTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Starts watching. Safe to call once; a missing key is retried in the background.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _wait is not null) return;
            _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => OnSignaled(), null, Timeout.Infinite, executeOnlyOnce: false);
            if (!TryArm()) ScheduleReopen();
        }
    }

    /// <summary>Opens the key if needed and registers for the next change. Caller holds <see cref="_gate"/>.</summary>
    private bool TryArm()
    {
        try
        {
            _key ??= Registry.CurrentUser.OpenSubKey(_subKeyPath, writable: false);
            if (_key is null) return false;

            var rc = RegNotifyChangeKeyValue(
                _key.Handle, false, RegNotifyChangeLastSet | RegNotifyChangeName | RegNotifyThreadAgnostic, _signal.SafeWaitHandle, true);
            if (rc == 0) return true;

            // Typically ERROR_KEY_DELETED (1018): drop the stale handle and try to open the key again.
            _key.Dispose();
            _key = null;
            _key = Registry.CurrentUser.OpenSubKey(_subKeyPath, writable: false);
            if (_key is null) return false;
            rc = RegNotifyChangeKeyValue(
                _key.Handle, false, RegNotifyChangeLastSet | RegNotifyChangeName | RegNotifyThreadAgnostic, _signal.SafeWaitHandle, true);
            if (rc == 0) return true;

            Log.Warn($"RegistryKeyWatcher: RegNotifyChangeKeyValue failed ({rc}) for HKCU\\{_subKeyPath}");
            _key.Dispose();
            _key = null;
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"RegistryKeyWatcher: cannot watch HKCU\\{_subKeyPath}", ex);
            _key?.Dispose();
            _key = null;
            return false;
        }
    }

    private void ScheduleReopen() => _reopenTimer.Change(ReopenInterval, Timeout.InfiniteTimeSpan);

    private void OnSignaled()
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Re-arm before notifying so that no change between the notification and the handler is lost.
            if (!TryArm()) ScheduleReopen();
        }
        Notify();
    }

    private void OnReopenTimer()
    {
        bool armed;
        lock (_gate)
        {
            if (_disposed) return;
            armed = TryArm();
            if (!armed) ScheduleReopen();
        }
        // The key (re)appeared: its value may differ from what the owner last read.
        if (armed) Notify();
    }

    private void Notify()
    {
        try { _onChanged(); }
        catch (Exception ex) { Log.Error($"RegistryKeyWatcher: change handler failed for HKCU\\{_subKeyPath}", ex); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _reopenTimer.Dispose();
            _wait?.Unregister(null);
            _wait = null;
            // Closing the key ends the pending notification registration.
            _key?.Dispose();
            _key = null;
        }
        _signal.Dispose();
    }

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);
}
