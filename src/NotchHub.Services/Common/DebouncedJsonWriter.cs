using NotchHub.Core;

namespace NotchHub.Services.Common;

/// <summary>
/// Persists the latest scheduled value to a JSON file on a background thread, coalescing bursts of changes
/// (e.g. dragging a to-do item) into one write. Callers never block on disk I/O; <see cref="Flush"/> writes
/// synchronously and is meant for shutdown.
/// </summary>
internal sealed class DebouncedJsonWriter<T> where T : class
{
    private readonly string _path;
    private readonly TimeSpan _delay;
    private readonly object _pendingGate = new();
    // Held while taking + writing a value so an older value can never overwrite a newer one on disk.
    private readonly object _writeGate = new();
    private T? _pending;
    private bool _scheduled;

    public DebouncedJsonWriter(string path, TimeSpan delay)
    {
        _path = path;
        _delay = delay;
    }

    public string Path => _path;

    public void Schedule(T value)
    {
        lock (_pendingGate)
        {
            _pending = value;
            if (_scheduled) return;
            _scheduled = true;
        }
        WriteLaterAsync().Forget($"DebouncedJsonWriter({System.IO.Path.GetFileName(_path)})");
    }

    /// <summary>Writes a pending value now (no-op when nothing is pending).</summary>
    public void Flush()
    {
        lock (_writeGate)
        {
            T? value;
            lock (_pendingGate)
            {
                value = _pending;
                _pending = null;
                _scheduled = false;
            }
            if (value is not null) JsonStore.Save(_path, value);
        }
    }

    private async Task WriteLaterAsync()
    {
        await Task.Delay(_delay).ConfigureAwait(false);
        Flush();
    }
}
