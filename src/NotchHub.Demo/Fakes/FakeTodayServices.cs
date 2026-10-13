using System.Diagnostics;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.Infrastructure;

namespace NotchHub.Demo.Fakes;

/// <summary>Demo weather: Warszawa, 21 °C, mainly clear, with hourly and 7-day forecast. No network.</summary>
public sealed class FakeWeatherService : FakeBackgroundService, IWeatherService
{
    private WeatherSnapshot _current = Build(DateTimeOffset.Now);

    /// <inheritdoc />
    public WeatherSnapshot? Current
    {
        get { lock (Gate) return _current; }
    }

    /// <inheritdoc />
    public bool IsLoading => false;

    /// <inheritdoc />
    public string? LastError => null;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken ct = default)
    {
        lock (Gate) _current = Build(DateTimeOffset.Now);
        Raise(Changed);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GeoLocation>> SearchLocationAsync(string query, CancellationToken ct = default)
    {
        GeoLocation[] all =
        [
            new("Warszawa", "Mazowieckie", "Polska", 52.2297, 21.0122),
            new("Kraków", "Małopolskie", "Polska", 50.0647, 19.9450),
            new("Gdańsk", "Pomorskie", "Polska", 54.3520, 18.6466),
            new("Wrocław", "Dolnośląskie", "Polska", 51.1079, 17.0385),
            new("Berlin", "Berlin", "Deutschland", 52.5200, 13.4050),
            new("London", "England", "United Kingdom", 51.5072, -0.1276),
        ];
        var q = query.Trim();
        IReadOnlyList<GeoLocation> result = q.Length == 0 ? [] :
            all.Where(l => l.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        return Task.FromResult(result);
    }

    private static WeatherSnapshot Build(DateTimeOffset now)
    {
        var hourStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        int[] codes = [1, 1, 2, 2, 3, 2, 1, 0, 0, 1, 2, 3, 61, 3, 2, 1, 0, 0, 0, 1, 1, 2, 2, 1];
        var hourly = Enumerable.Range(0, 24).Select(i =>
        {
            var t = hourStart.AddHours(i);
            var h = t.Hour + t.Minute / 60.0;
            var temp = 15 + 6 * Math.Cos((h - 15) / 24 * 2 * Math.PI);
            return new WeatherHour(t, Math.Round(temp, 1), codes[i], t.Hour is >= 7 and < 19, codes[i] == 61 ? 55 : i % 5 * 5);
        }).ToArray();
        var today = DemoTime.Today;
        (double Min, double Max, int Code, double Rain)[] days =
            [(12, 23, 1, 5), (11, 20, 3, 20), (10, 17, 61, 70), (9, 16, 63, 80), (10, 18, 2, 15), (12, 21, 1, 5), (13, 22, 0, 0)];
        var daily = days.Select((d, i) =>
        {
            var date = today.AddDays(i);
            return new WeatherDay(date, d.Min, d.Max, d.Code, d.Rain, DemoTime.At(date, 7, 4 + i), DemoTime.At(date, 18, 21 - i * 2));
        }).ToArray();
        return new WeatherSnapshot(now, "Warszawa", 21, 20, 1, true, 48, 9, 12, 23, hourly, daily);
    }
}

/// <summary>Demo to-do list (in memory).</summary>
public sealed class FakeTodoService : FakeBackgroundService, ITodoService
{
    private readonly List<TodoItem> _items;

    /// <summary>Seeds three items.</summary>
    public FakeTodoService()
    {
        var now = DateTimeOffset.Now;
        _items =
        [
            new(Guid.NewGuid(), "Buy Hitodama!", true, now.AddHours(-5), now.AddMinutes(-40)),
            new(Guid.NewGuid(), "Finish NotchHub settings", false, now.AddHours(-3), null),
            new(Guid.NewGuid(), "Call mom", false, now.AddHours(-1), null),
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<TodoItem> Items
    {
        get { lock (Gate) return _items.ToArray(); }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public TodoItem Add(string text)
    {
        var item = new TodoItem(Guid.NewGuid(), text.Trim(), false, DateTimeOffset.Now, null);
        Mutate(l => { l.Add(item); return true; });
        return item;
    }

    /// <inheritdoc />
    public void SetDone(Guid id, bool done) => Replace(id, i => i with { IsDone = done, CompletedAt = done ? DateTimeOffset.Now : null });

    /// <inheritdoc />
    public void Rename(Guid id, string text) => Replace(id, i => i with { Text = text.Trim() });

    /// <inheritdoc />
    public void Remove(Guid id) => Mutate(l => l.RemoveAll(i => i.Id == id) > 0);

    /// <inheritdoc />
    public void Move(Guid id, int newIndex) => Mutate(l =>
    {
        var index = l.FindIndex(i => i.Id == id);
        if (index < 0) return false;
        var item = l[index];
        l.RemoveAt(index);
        l.Insert(Math.Clamp(newIndex, 0, l.Count), item);
        return true;
    });

    /// <inheritdoc />
    public void ClearCompleted() => Mutate(l => l.RemoveAll(i => i.IsDone) > 0);

    private void Replace(Guid id, Func<TodoItem, TodoItem> change) => Mutate(l =>
    {
        var index = l.FindIndex(i => i.Id == id);
        if (index < 0) return false;
        l[index] = change(l[index]);
        return true;
    });

    private void Mutate(Func<List<TodoItem>, bool> change)
    {
        bool changed;
        lock (Gate) changed = change(_items);
        if (changed) Raise(Changed);
    }
}

/// <summary>Demo countdown timer with real semantics (ticks ~250 ms only while running).</summary>
public sealed class FakeTimerService : FakeBackgroundService, ITimerService
{
    private readonly Stopwatch _watch = new();
    private Timer? _ticker;
    private TimerState _state;
    private TimeSpan _duration;
    private TimeSpan _budget; // remaining at the moment _watch was last reset

    /// <inheritdoc />
    public TimerState State
    {
        get { lock (Gate) return _state; }
    }

    /// <inheritdoc />
    public TimeSpan Duration
    {
        get { lock (Gate) return _duration; }
    }

    /// <inheritdoc />
    public TimeSpan Remaining
    {
        get { lock (Gate) return RemainingLocked(); }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler? Finished;

    /// <inheritdoc />
    public void Start(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        lock (Gate)
        {
            _duration = _budget = duration;
            _watch.Restart();
            _state = TimerState.Running;
            EnsureTicker(true);
        }
        Raise(Changed);
    }

    /// <inheritdoc />
    public void Pause() => Transition(TimerState.Running, () =>
    {
        _budget = RemainingLocked();
        _watch.Reset();
        _state = TimerState.Paused;
        EnsureTicker(false);
    });

    /// <inheritdoc />
    public void Resume() => Transition(TimerState.Paused, () =>
    {
        _watch.Restart();
        _state = TimerState.Running;
        EnsureTicker(true);
    });

    /// <inheritdoc />
    public void Reset()
    {
        lock (Gate)
        {
            _state = TimerState.Idle;
            _duration = _budget = TimeSpan.Zero;
            _watch.Reset();
            EnsureTicker(false);
        }
        Raise(Changed);
    }

    /// <inheritdoc />
    public void AddTime(TimeSpan delta)
    {
        lock (Gate)
        {
            if (_state is not (TimerState.Running or TimerState.Paused)) return;
            var remaining = RemainingLocked() + delta;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            _duration = TimeSpan.FromTicks(Math.Max(remaining.Ticks, _duration.Ticks + delta.Ticks));
            _budget = remaining;
            if (_state == TimerState.Running) _watch.Restart();
        }
        Raise(Changed);
        Tick(null);
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        lock (Gate) EnsureTicker(false);
    }

    private void Transition(TimerState from, Action change)
    {
        lock (Gate)
        {
            if (_state != from) return;
            change();
        }
        Raise(Changed);
    }

    private TimeSpan RemainingLocked()
    {
        var r = _budget - _watch.Elapsed;
        return r < TimeSpan.Zero ? TimeSpan.Zero : r;
    }

    private void EnsureTicker(bool run)
    {
        if (run && !IsDisposed) _ticker ??= new Timer(Tick, null, 250, 250);
        else if (!run && _ticker is not null)
        {
            _ticker.Dispose();
            _ticker = null;
        }
    }

    private void Tick(object? _)
    {
        var finished = false;
        lock (Gate)
        {
            if (_state != TimerState.Running) return;
            if (RemainingLocked() <= TimeSpan.Zero)
            {
                _state = TimerState.Finished;
                _budget = TimeSpan.Zero;
                _watch.Reset();
                EnsureTicker(false);
                finished = true;
            }
        }
        Raise(Changed);
        if (finished)
        {
            Log.Info("DEMO: timer finished");
            Raise(Finished);
        }
    }
}
