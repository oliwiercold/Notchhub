using NotchHub.Core;

namespace NotchHub.Platform.Media.Common;

/// <summary>
/// Collapses bursts of <see cref="Trigger"/> calls into one invocation of an action, run on the thread pool
/// <c>delay</c> after the first trigger of the burst. Invocations never overlap: triggers that arrive while the
/// action runs schedule exactly one more run afterwards. Thread-safe; exceptions from the action are logged.
/// </summary>
internal sealed class CoalescingTimer : IDisposable
{
    private readonly object _gate = new();
    private readonly Action _action;
    private readonly TimeSpan _delay;
    private readonly string _context;
    private readonly Timer _timer;
    private bool _armed;
    private bool _running;
    private bool _rerun;
    private bool _disposed;

    public CoalescingTimer(TimeSpan delay, Action action, string context)
    {
        _delay = delay;
        _action = action;
        _context = context;
        _timer = new Timer(OnTick);
    }

    public void Trigger()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_running)
            {
                _rerun = true;
                return;
            }
            if (_armed) return;
            _armed = true;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTick(object? state)
    {
        lock (_gate)
        {
            _armed = false;
            if (_disposed) return;
            if (_running)
            {
                _rerun = true;
                return;
            }
            _running = true;
        }

        try
        {
            _action();
        }
        catch (Exception ex)
        {
            Log.Error(_context, ex);
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
                if (_rerun && !_disposed)
                {
                    _rerun = false;
                    _armed = true;
                    _timer.Change(_delay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }
}
