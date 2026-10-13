using System.Diagnostics;
using NotchHub.Core;

namespace NotchHub.Platform.Media.Audio;

/// <summary>
/// Rate-limits volume writes coming from slider drags: the first value is written at once, then at most one write
/// per interval, and the latest requested value always wins (trailing write). Thread-safe.
/// </summary>
internal sealed class VolumeWriteThrottle : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<float> _write;
    private readonly TimeSpan _interval;
    private readonly Timer _timer;
    private float? _pending;
    private long _lastWrite;
    private long _lastRequest;
    private bool _windowOpen;
    private bool _disposed;

    /// <param name="interval">Minimum time between two writes.</param>
    /// <param name="write">Must not block (it is called under the throttle lock to keep writes ordered).</param>
    public VolumeWriteThrottle(TimeSpan interval, Action<float> write)
    {
        _interval = interval;
        _write = write;
        _timer = new Timer(OnWindowElapsed);
    }

    public void Request(float value)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _lastRequest = Stopwatch.GetTimestamp();
            if (_windowOpen)
            {
                _pending = value;
                return;
            }

            var sinceLastWrite = _lastWrite == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(_lastWrite);
            if (sinceLastWrite >= _interval)
            {
                WriteLocked(value);
            }
            else
            {
                _pending = value;
                OpenWindowLocked(_interval - sinceLastWrite);
            }
        }
    }

    /// <summary>
    /// True while writes are still queued or were requested within <paramref name="grace"/>: notifications that arrive
    /// meanwhile may describe an intermediate value and should not overwrite the optimistic one.
    /// </summary>
    public bool IsSettling(TimeSpan grace)
    {
        lock (_gate)
        {
            return _pending is not null || (_lastRequest != 0 && Stopwatch.GetElapsedTime(_lastRequest) < grace);
        }
    }

    private void OnWindowElapsed(object? state)
    {
        lock (_gate)
        {
            _windowOpen = false;
            if (_disposed || _pending is not { } value) return;
            _pending = null;
            WriteLocked(value);
        }
    }

    private void WriteLocked(float value)
    {
        _lastWrite = Stopwatch.GetTimestamp();
        OpenWindowLocked(_interval);
        try
        {
            _write(value);
        }
        catch (Exception ex)
        {
            Log.Error("Audio: volume write failed", ex);
        }
    }

    private void OpenWindowLocked(TimeSpan duration)
    {
        _windowOpen = true;
        _timer.Change(duration, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = null;
            _timer.Dispose();
        }
    }
}
