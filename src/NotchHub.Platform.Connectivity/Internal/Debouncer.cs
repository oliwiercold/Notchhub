using NotchHub.Core;

namespace NotchHub.Platform.Connectivity.Internal;

/// <summary>
/// Coalesces bursts of triggers into a single invocation of an action on the thread pool, <see cref="Delay"/> after
/// the last trigger. Thread-safe; exceptions thrown by the action are logged, never propagated.
/// </summary>
internal sealed class Debouncer : IDisposable
{
    private readonly Action _action;
    private readonly string _context;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _disposed;

    public Debouncer(TimeSpan delay, Action action, string context)
    {
        Delay = delay;
        _action = action;
        _context = context;
        _timer = new Timer(_ => Run(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public TimeSpan Delay { get; }

    /// <summary>(Re)starts the delay; the action runs once after the burst settles.</summary>
    public void Trigger()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Run()
    {
        lock (_gate)
        {
            if (_disposed) return;
        }

        try { _action(); }
        catch (Exception ex) { Log.Error($"{_context}: debounced action failed", ex); }
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
