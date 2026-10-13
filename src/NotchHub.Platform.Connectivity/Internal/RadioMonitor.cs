using NotchHub.Core;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace NotchHub.Platform.Connectivity.Internal;

/// <summary>
/// Tracks the system radio of one <see cref="RadioKind"/> (Wi-Fi / Bluetooth) through <c>Windows.Devices.Radios</c>:
/// presence, on/off state and whether this app may toggle it. Radios that appear or disappear later (USB dongles,
/// driver restarts) are picked up through a device watcher. All members are thread-safe; <see cref="Changed"/> fires on
/// arbitrary threads.
/// </summary>
internal sealed class RadioMonitor : IDisposable
{
    private readonly RadioKind _kind;
    private readonly object _gate = new();
    private readonly Debouncer _resolveDebouncer;
    private readonly SemaphoreSlim _resolveGate = new(1, 1);
    private RadioAccessStatus _access = RadioAccessStatus.Unspecified;
    private Radio? _radio;
    private DeviceWatcher? _watcher;
    private bool _disposed;

    public RadioMonitor(RadioKind kind)
    {
        _kind = kind;
        _resolveDebouncer = new Debouncer(TimeSpan.FromMilliseconds(400), () => ResolveAsync().Forget($"RadioMonitor({kind}).Resolve"), $"RadioMonitor({kind})");
    }

    /// <summary>Raised when presence, state or access changes.</summary>
    public event Action? Changed;

    public bool IsPresent { get { lock (_gate) return _radio is not null; } }

    public RadioState State
    {
        get
        {
            lock (_gate)
            {
                if (_radio is null) return RadioState.Unknown;
                try { return _radio.State; }
                catch (Exception) { return RadioState.Unknown; }
            }
        }
    }

    public bool IsOn => State == RadioState.On;

    /// <summary>The radio exists, the user/system allows this app to control it and it is not hardware-disabled.</summary>
    public bool CanToggle
    {
        get
        {
            lock (_gate)
            {
                if (_radio is null || _access != RadioAccessStatus.Allowed) return false;
                try { return _radio.State is RadioState.On or RadioState.Off; }
                catch (Exception) { return false; }
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var access = await Radio.RequestAccessAsync().AsTask(cancellationToken).ConfigureAwait(false);
            lock (_gate) _access = access;
            if (access != RadioAccessStatus.Allowed) Log.Warn($"RadioMonitor({_kind}): radio access is {access}; toggling disabled");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warn($"RadioMonitor({_kind}): RequestAccessAsync failed", ex);
        }

        await ResolveAsync().ConfigureAwait(false);

        try
        {
            var watcher = DeviceInformation.CreateWatcher(Radio.GetDeviceSelector());
            watcher.Added += (_, _) => _resolveDebouncer.Trigger();
            watcher.Removed += (_, _) => _resolveDebouncer.Trigger();
            // A DeviceWatcher only delivers Added events reliably when Updated is subscribed as well.
            watcher.Updated += (_, _) => { };
            lock (_gate)
            {
                if (_disposed) return;
                _watcher = watcher;
            }
            watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Warn($"RadioMonitor({_kind}): radio device watcher unavailable", ex);
        }
    }

    /// <summary>Switches the radio on/off. Returns false when there is no radio or access is denied.</summary>
    public async Task<bool> SetStateAsync(bool on)
    {
        Radio? radio;
        lock (_gate) radio = _radio;
        if (radio is null || !CanToggle) return false;

        try
        {
            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off).AsTask().ConfigureAwait(false);
            if (result != RadioAccessStatus.Allowed)
            {
                Log.Warn($"RadioMonitor({_kind}): SetStateAsync({on}) returned {result}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"RadioMonitor({_kind}): SetStateAsync({on}) failed", ex);
            return false;
        }
    }

    private async Task ResolveAsync()
    {
        await _resolveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Radio? found = null;
            try
            {
                var radios = await Radio.GetRadiosAsync().AsTask().ConfigureAwait(false);
                // Prefer a radio that is on when several adapters of the same kind exist.
                found = radios.Where(r => r.Kind == _kind).OrderByDescending(r => r.State == RadioState.On).FirstOrDefault();
            }
            catch (Exception ex)
            {
                Log.Warn($"RadioMonitor({_kind}): GetRadiosAsync failed", ex);
            }

            bool changed;
            lock (_gate)
            {
                if (_disposed) return;
                changed = !SameRadio(_radio, found);
                if (changed)
                {
                    if (_radio is not null) _radio.StateChanged -= OnStateChanged;
                    _radio = found;
                    if (_radio is not null) _radio.StateChanged += OnStateChanged;
                }
            }

            if (changed)
            {
                Log.Info($"RadioMonitor({_kind}): radio {(found is null ? "not present" : $"'{found.Name}' state={found.State}")}");
                RaiseChanged();
            }
        }
        finally
        {
            _resolveGate.Release();
        }
    }

    private static bool SameRadio(Radio? a, Radio? b)
    {
        if (a is null || b is null) return a is null && b is null;
        // Radio objects are re-created by every GetRadiosAsync call; compare by name + kind.
        return a.Kind == b.Kind && string.Equals(a.Name, b.Name, StringComparison.Ordinal);
    }

    private void OnStateChanged(Radio sender, object args) => RaiseChanged();

    private void RaiseChanged()
    {
        lock (_gate)
        {
            if (_disposed) return;
        }
        try { Changed?.Invoke(); }
        catch (Exception ex) { Log.Error($"RadioMonitor({_kind}): Changed handler failed", ex); }
    }

    public void Dispose()
    {
        DeviceWatcher? watcher;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_radio is not null) _radio.StateChanged -= OnStateChanged;
            _radio = null;
            watcher = _watcher;
            _watcher = null;
        }
        _resolveDebouncer.Dispose();
        try
        {
            if (watcher is not null && watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
        }
        catch (Exception ex)
        {
            Log.Warn($"RadioMonitor({_kind}): stopping watcher failed", ex);
        }
    }
}
