using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Clipboard;

public enum ClipFilter { All, Text, Images, Links, Code }

/// <summary>ObservableCollection that can replace its contents with a single Reset notification.</summary>
public sealed class BulkCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public sealed partial class ClipboardViewModel : ObservableObject
{
    private bool _shown;
    private string _normalizedQuery = "";
    private List<Guid> _selectedIds = new();

    public BulkCollection<ClipEntry> Entries { get; } = new();

    /// <summary>Raised after <see cref="Entries"/> was rebuilt; the view re-applies the selection (entries to select, primary).</summary>
    public event Action<IReadOnlyList<ClipEntry>, ClipEntry?>? SelectionRestore;

    public bool IsRebuilding { get; private set; }

    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial ClipFilter Filter { get; set; } = ClipFilter.All;
    [ObservableProperty] public partial ClipEntry? Current { get; set; }
    [ObservableProperty] public partial int SelectionCount { get; set; }
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool NoResults { get; set; }
    [ObservableProperty] public partial bool ConfirmingClear { get; set; }
    [ObservableProperty] public partial string SearchPlaceholder { get; set; } = "";

    public string SelectionText => Loc.Format("clipboard.selected", SelectionCount);

    partial void OnQueryChanged(string value)
    {
        _normalizedQuery = ClipEntry.Normalize(value.Trim());
        if (_shown) Rebuild();
    }

    partial void OnFilterChanged(ClipFilter value)
    {
        if (_shown) Rebuild();
    }

    public bool IsMulti => SelectionCount > 1;

    partial void OnSelectionCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(IsMulti));
    }

    public void OnShown()
    {
        if (_shown) return;
        _shown = true;
        if (AppServices.Clipboard is { } svc) svc.Changed += OnServiceChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        Rebuild();
    }

    public void OnHidden()
    {
        if (!_shown) return;
        _shown = false;
        if (AppServices.Clipboard is { } svc) svc.Changed -= OnServiceChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
        ConfirmingClear = false;
    }

    private void OnServiceChanged(object? sender, EventArgs e) => AppServices.Ui.Post(() => { if (_shown) Rebuild(); });
    private void OnLanguageChanged(object? sender, EventArgs e) => AppServices.Ui.Post(() => { if (_shown) Rebuild(force: true); });

    /// <summary>Called by the view whenever the ListBox selection changes.</summary>
    public void SetSelection(IReadOnlyList<ClipEntry> selected, ClipEntry? primary)
    {
        if (IsRebuilding) return;
        _selectedIds = selected.Select(s => s.Id).ToList();
        SelectionCount = selected.Count;
        Current = primary;
    }

    private readonly Dictionary<Guid, ClipEntry> _cache = new();

    private void Rebuild(bool force = false)
    {
        var items = AppServices.Clipboard?.Items ?? Array.Empty<ClipItem>();
        TotalCount = items.Count;
        IsEmpty = items.Count == 0;
        SearchPlaceholder = items.Count == 1 ? Loc.Get("clipboard.searchOne") : Loc.Format("clipboard.search", items.Count);
        if (IsEmpty) ConfirmingClear = false;

        var today = DateTime.Today;
        string GroupOf(ClipItem i)
        {
            if (i.IsPinned) return Loc.Get("clipboard.group.pinned");
            var d = (today - i.CopiedAt.ToLocalTime().Date).Days;
            return Loc.Get(d <= 0 ? "clipboard.group.today" : d == 1 ? "clipboard.group.yesterday" : "clipboard.group.older");
        }

        if (force) _cache.Clear();
        var alive = new HashSet<Guid>();
        var list = new List<ClipEntry>(items.Count);
        foreach (var it in items.Where(i => i.IsPinned).Concat(items.Where(i => !i.IsPinned)))
        {
            if (!PassesFilter(it.Kind)) continue;
            var group = GroupOf(it);
            if (!_cache.TryGetValue(it.Id, out var e) || !ReferenceEquals(e.Item, it) || e.Group != group)
                _cache[it.Id] = e = new ClipEntry(it, group);
            alive.Add(it.Id);
            if (e.Matches(_normalizedQuery)) list.Add(e);
        }
        foreach (var dead in _cache.Keys.Where(k => !alive.Contains(k)).ToList()) _cache.Remove(dead);

        NoResults = !IsEmpty && list.Count == 0;

        var sel = list.Where(e => _selectedIds.Contains(e.Id)).ToList();
        var primary = Current is { } c ? sel.FirstOrDefault(e => e.Id == c.Id) : null;
        if (sel.Count == 0 && list.Count > 0)
        {
            // keep the cursor near the old position if the selected item disappeared
            sel.Add(list[0]);
        }
        primary ??= sel.FirstOrDefault();

        IsRebuilding = true;
        try { Entries.ReplaceAll(list); }
        finally { IsRebuilding = false; }
        SelectionRestore?.Invoke(sel, primary);
    }

    private bool PassesFilter(ClipKind k) => Filter switch
    {
        ClipFilter.Text => k is ClipKind.Text or ClipKind.Code or ClipKind.Color,
        ClipFilter.Images => k == ClipKind.Image,
        ClipFilter.Links => k == ClipKind.Link,
        ClipFilter.Code => k == ClipKind.Code,
        _ => true,
    };

    public void CycleFilter(int dir)
    {
        var n = Enum.GetValues<ClipFilter>().Length;
        Filter = (ClipFilter)(((int)Filter + dir + n) % n);
    }

    private IReadOnlyList<Guid> Targets(ClipEntry? e) =>
        e is not null && !_selectedIds.Contains(e.Id) ? new[] { e.Id } : _selectedIds.ToArray();

    // ─────────── commands ───────────

    [RelayCommand]
    private void Paste(ClipEntry? e = null)
    {
        var ids = Targets(e);
        if (ids.Count == 0 || AppServices.Clipboard is null) return;
        AppServices.Clipboard.PasteAsync(ids).Forget("clipboard.paste");
    }

    [RelayCommand]
    private void Copy(ClipEntry? e = null)
    {
        var ids = Targets(e);
        if (ids.Count == 0 || AppServices.Clipboard is null) return;
        AppServices.Clipboard.CopyToClipboardAsync(ids).Forget("clipboard.copy");
    }

    [RelayCommand]
    private void TogglePin(ClipEntry? e = null)
    {
        if (AppServices.Clipboard is not { } svc) return;
        var ids = Targets(e);
        if (ids.Count == 0) return;
        // pin all if any of them is unpinned, else unpin all
        var items = svc.Items.Where(i => ids.Contains(i.Id)).ToList();
        var pin = items.Any(i => !i.IsPinned);
        foreach (var i in items) svc.SetPinned(i.Id, pin);
    }

    [RelayCommand]
    private void Delete(ClipEntry? e = null)
    {
        if (AppServices.Clipboard is not { } svc) return;
        var ids = Targets(e);
        if (ids.Count == 0) return;

        // move the cursor to the neighbour of the removed block
        var idx = Entries.Select((x, i) => (x, i)).Where(t => ids.Contains(t.x.Id)).Select(t => t.i).DefaultIfEmpty(-1).Max();
        var next = Entries.Skip(idx + 1).FirstOrDefault(x => !ids.Contains(x.Id))
                   ?? Entries.Take(Math.Max(0, idx)).LastOrDefault(x => !ids.Contains(x.Id));
        _selectedIds = next is null ? new() : new() { next.Id };
        Current = next;

        if (ids.Count == 1) svc.Delete(ids[0]); else svc.Delete(ids);
    }

    [RelayCommand]
    private void AskClear() => ConfirmingClear = true;

    [RelayCommand]
    private void CancelClear() => ConfirmingClear = false;

    [RelayCommand]
    private void ConfirmClear()
    {
        ConfirmingClear = false;
        AppServices.Clipboard?.Clear(keepPinned: true);
    }

    [RelayCommand]
    private void OpenSettings() => AppServices.Shell?.Expand(ShellTab.Settings);

    [RelayCommand]
    private void OpenLink(ClipEntry? e)
    {
        var text = (e ?? Current)?.FullText.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            AppServices.Shell?.Collapse();
        }
        catch (Exception ex) { Log.Warn("Clipboard: cannot open link", ex); }
    }
}
