using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;

namespace NotchHub.Features.Today;

/// <summary>Selectable option (chips, swatches). IsSelected always notifies so toggle visuals snap back to the VM.</summary>
public sealed class Choice<T> : ObservableObject
{
    private bool _isSelected;

    public Choice(string label, T value, Brush? brush = null)
    {
        Label = label;
        Value = value;
        Brush = brush;
    }

    public string Label { get; }
    public T Value { get; }
    public Brush? Brush { get; }
    public bool HasBrush => Brush is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }
}

public sealed partial class DayItem : ObservableObject
{
    public required DateOnly Date { get; init; }
    public required string Name { get; init; }
    public string Number => Date.Day.ToString();
    [ObservableProperty] private bool _isToday;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _hasEvents;
}

public sealed partial class EventItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Header { get; init; }
    public required string Title { get; init; }
    public required bool IsLocal { get; init; }
    /// <summary>Local events and events of writable Google calendars can be deleted.</summary>
    public bool CanDelete { get; init; }
    public Brush? ColorBrush { get; init; }
    public bool HasColor => ColorBrush is not null;
    // ── expanded day agenda only ──
    public string EndText { get; init; } = "";
    public bool HasEnd => EndText.Length > 0;
    public string? Location { get; init; }
    public bool HasLocation => Location is not null;
    /// <summary>"Google" / "ICS" / "Lokalny".</summary>
    public string SourceLabel { get; init; } = "";
    /// <summary>Calendar name (tooltip).</summary>
    public string SourceName { get; init; } = "";
    public bool IsNow { get; init; }
    public bool IsPast { get; init; }
    [ObservableProperty] private bool _showDelete;

    [RelayCommand]
    private async Task Delete()
    {
        if (!CanDelete) return;
        ShowDelete = false;
        try
        {
            if (!await AppServices.Calendar.RemoveEventAsync(Id)) Log.Warn("Today: event could not be deleted");
        }
        catch (Exception ex)
        {
            Log.Warn("Today: deleting an event failed", ex);
        }
    }
}

public sealed partial class TodoVm : ObservableObject
{
    private bool _syncing;

    public TodoVm(Guid id) => Id = id;

    public Guid Id { get; }
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = "";

    internal void Sync(string text, bool done)
    {
        _syncing = true;
        Text = text;
        IsDone = done;
        _syncing = false;
    }

    partial void OnIsDoneChanged(bool value)
    {
        if (!_syncing) AppServices.Todos.SetDone(Id, value);
    }

    [RelayCommand] private void Delete() => AppServices.Todos.Remove(Id);

    public void BeginEdit()
    {
        EditText = Text;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        if (!IsEditing) return;
        IsEditing = false;
        var t = EditText.Trim();
        if (t.Length > 0 && t != Text) AppServices.Todos.Rename(Id, t);
    }

    public void CancelEdit() => IsEditing = false;
}

internal static class BrushUtil
{
    public static Brush? FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
        catch (FormatException) { return null; }
    }
}
