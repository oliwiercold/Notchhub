using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Services.Common;

namespace NotchHub.Services.Todos;

/// <summary>
/// To-do list persisted in todos.json. Order: active items (user order, new ones appended), then completed ones
/// (most recently completed first). Items completed before today (local time) are removed at load and at every
/// local midnight.
/// </summary>
public sealed class TodoService : ITodoService
{
    private readonly TimeProvider _time;
    private readonly DebouncedJsonWriter<TodoFile> _writer;
    private readonly object _gate = new();
    private readonly List<TodoItem> _items;
    private IReadOnlyList<TodoItem> _snapshot;
    private ITimer? _midnightTimer;
    private bool _disposed;

    public TodoService() : this(AppPaths.File("todos.json"), TimeProvider.System) { }

    internal TodoService(string path, TimeProvider time)
    {
        _time = time;
        _writer = new DebouncedJsonWriter<TodoFile>(path, TimeSpan.FromMilliseconds(400));
        var file = JsonStore.Load(path, () => new TodoFile());
        _items = (file.Items ?? []).Where(i => i is not null && i.Id != Guid.Empty).DistinctBy(i => i.Id).ToList();
        _snapshot = _items.ToArray();
        RemoveExpired();
    }

    public IReadOnlyList<TodoItem> Items => Volatile.Read(ref _snapshot);

    public event EventHandler? Changed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        RemoveExpired();
        ScheduleMidnight();
        return Task.CompletedTask;
    }

    public TodoItem Add(string text)
    {
        var item = new TodoItem(Guid.NewGuid(), (text ?? "").Trim(), false, _time.GetLocalNow(), null);
        Mutate(list =>
        {
            list.Insert(FirstDoneIndex(list), item);
            return true;
        });
        return item;
    }

    public void SetDone(Guid id, bool done) => Mutate(list =>
    {
        var index = list.FindIndex(i => i.Id == id);
        if (index < 0 || list[index].IsDone == done) return false;
        var item = list[index] with { IsDone = done, CompletedAt = done ? _time.GetLocalNow() : null };
        list.RemoveAt(index);
        // Completed: top of the completed section. Reopened: end of the active section.
        list.Insert(FirstDoneIndex(list), item);
        return true;
    });

    public void Rename(Guid id, string text) => Mutate(list =>
    {
        var index = list.FindIndex(i => i.Id == id);
        var trimmed = (text ?? "").Trim();
        if (index < 0 || list[index].Text == trimmed) return false;
        list[index] = list[index] with { Text = trimmed };
        return true;
    });

    public void Remove(Guid id) => Mutate(list => list.RemoveAll(i => i.Id == id) > 0);

    public void Move(Guid id, int newIndex) => Mutate(list =>
    {
        var index = list.FindIndex(i => i.Id == id);
        if (index < 0) return false;
        var item = list[index];
        list.RemoveAt(index);
        newIndex = Math.Clamp(newIndex, 0, list.Count);
        list.Insert(newIndex, item);
        return newIndex != index;
    });

    public void ClearCompleted() => Mutate(list => list.RemoveAll(i => i.IsDone) > 0);

    /// <summary>Removes items completed before today's local date (tests call it with a fake clock).</summary>
    internal void RemoveExpired()
    {
        var tz = _time.LocalTimeZone;
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        Mutate(list => list.RemoveAll(i =>
            i.IsDone && i.CompletedAt is { } at && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, tz).DateTime) < today) > 0);
    }

    private void ScheduleMidnight()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var now = _time.GetLocalNow();
            var midnight = now.Date.AddDays(1);
            var due = new DateTimeOffset(midnight, _time.LocalTimeZone.GetUtcOffset(midnight)) - now;
            due = due <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : due + TimeSpan.FromSeconds(2);
            _midnightTimer?.Dispose();
            _midnightTimer = _time.CreateTimer(static s => ((TodoService)s!).OnMidnight(), this, due, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnMidnight()
    {
        Log.Safe("TodoService.OnMidnight", RemoveExpired);
        ScheduleMidnight();
    }

    private static int FirstDoneIndex(List<TodoItem> list)
    {
        var index = list.FindIndex(i => i.IsDone);
        return index < 0 ? list.Count : index;
    }

    private void Mutate(Func<List<TodoItem>, bool> change)
    {
        lock (_gate)
        {
            if (!change(_items)) return;
            Volatile.Write(ref _snapshot, _items.ToArray());
            _writer.Schedule(new TodoFile { Items = [.. _items] });
        }
        SafeEvents.Raise(Changed, this, "TodoService.Changed");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _midnightTimer?.Dispose();
            _midnightTimer = null;
        }
        _writer.Flush();
    }

    internal sealed class TodoFile
    {
        public int Version { get; set; } = 1;
        public List<TodoItem>? Items { get; set; } = [];
    }
}
