using NotchHub.Core.Contracts;
using NotchHub.Services.Common;

namespace NotchHub.Services.Timer;

/// <summary>
/// Countdown timer. The end time is kept as an absolute UTC instant (sleep or tick drift cannot skew it);
/// a ~4 Hz tick timer exists only while the countdown is running.
/// </summary>
public sealed class TimerService : ITimerService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private TimerState _state = TimerState.Idle;
    private TimeSpan _duration;
    private DateTimeOffset _endUtc;          // valid while Running
    private TimeSpan _pausedRemaining;       // valid while Paused
    private ITimer? _ticker;
    private bool _disposed;

    public TimerService() : this(TimeProvider.System) { }

    internal TimerService(TimeProvider time) => _time = time;

    public TimerState State { get { lock (_gate) return _state; } }

    public TimeSpan Duration { get { lock (_gate) return _duration; } }

    public TimeSpan Remaining
    {
        get
        {
            lock (_gate)
            {
                return _state switch
                {
                    TimerState.Running => NonNegative(_endUtc - _time.GetUtcNow()),
                    TimerState.Paused => _pausedRemaining,
                    _ => TimeSpan.Zero,
                };
            }
        }
    }

    public event EventHandler? Changed;
    public event EventHandler? Finished;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Start(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        lock (_gate)
        {
            if (_disposed) return;
            _duration = duration;
            _endUtc = _time.GetUtcNow() + duration;
            _state = TimerState.Running;
            EnsureTicker();
        }
        RaiseChanged();
    }

    public void Pause()
    {
        if (CheckFinished()) return;
        lock (_gate)
        {
            if (_state != TimerState.Running) return;
            _pausedRemaining = NonNegative(_endUtc - _time.GetUtcNow());
            _state = TimerState.Paused;
            StopTicker();
        }
        RaiseChanged();
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_state != TimerState.Paused || _disposed) return;
            _endUtc = _time.GetUtcNow() + _pausedRemaining;
            _state = TimerState.Running;
            EnsureTicker();
        }
        RaiseChanged();
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_state == TimerState.Idle && _duration == TimeSpan.Zero) return;
            _state = TimerState.Idle;
            _duration = TimeSpan.Zero;
            _pausedRemaining = TimeSpan.Zero;
            StopTicker();
        }
        RaiseChanged();
    }

    /// <summary>Adds (or subtracts) time; on an idle/finished timer a positive delta starts a new countdown.</summary>
    public void AddTime(TimeSpan delta)
    {
        if (delta == TimeSpan.Zero) return;
        TimerState state;
        lock (_gate) state = _state;
        if (state is TimerState.Idle or TimerState.Finished)
        {
            if (delta > TimeSpan.Zero) Start(delta);
            return;
        }

        lock (_gate)
        {
            if (_state == TimerState.Running)
            {
                _endUtc += delta;
                _duration = NonNegative(_duration + delta);
            }
            else if (_state == TimerState.Paused)
            {
                _pausedRemaining = NonNegative(_pausedRemaining + delta);
                _duration = NonNegative(_duration + delta);
            }
        }
        if (!CheckFinished()) RaiseChanged();
    }

    /// <summary>Tick timer callback (tests call it directly with a fake clock).</summary>
    internal void Tick()
    {
        if (!CheckFinished()) RaiseChanged();
    }

    /// <summary>Running → Finished once the end time has passed. True when it finished in this call.</summary>
    private bool CheckFinished()
    {
        lock (_gate)
        {
            if (_state != TimerState.Running || _time.GetUtcNow() < _endUtc) return false;
            _state = TimerState.Finished;
            StopTicker();
        }
        RaiseChanged();
        SafeEvents.Raise(Finished, this, "TimerService.Finished");
        return true;
    }

    private void EnsureTicker() =>
        _ticker ??= _time.CreateTimer(static s => ((TimerService)s!).Tick(), this, TickInterval, TickInterval);

    private void StopTicker()
    {
        _ticker?.Dispose();
        _ticker = null;
    }

    private void RaiseChanged() => SafeEvents.Raise(Changed, this, "TimerService.Changed");

    private static TimeSpan NonNegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopTicker();
        }
    }
}
