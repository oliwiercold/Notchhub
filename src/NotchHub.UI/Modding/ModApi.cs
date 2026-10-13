using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NotchHub.UI.Modding;

/// <summary>
/// Entry point of a C# ("assembly") mod. A mod DLL contains one or more public classes implementing this interface with a
/// parameterless constructor. <see cref="Initialize"/> runs once on the UI thread at startup. See docs/MODDING.md.
/// </summary>
public interface INotchMod
{
    /// <summary>Called once on the UI thread. Register tabs / tiles here; use NotchHub.Core.AppServices for the app's services.</summary>
    void Initialize(IModContext context);

    /// <summary>Called when the app exits.</summary>
    void Shutdown() { }
}

/// <summary>What a mod can do. Passed to <see cref="INotchMod.Initialize"/>.</summary>
public interface IModContext
{
    /// <summary>Id from mod.json.</summary>
    string ModId { get; }
    /// <summary>The mod's own folder (where mod.json lives) — for loading its assets.</summary>
    string ModDirectory { get; }
    /// <summary>A writable folder for the mod's data (%APPDATA%\NotchHub\mods-data\&lt;id&gt;).</summary>
    string DataDirectory { get; }

    /// <summary>Adds a tab to the notch panel (shown after the built-in tabs).</summary>
    void AddTab(ModTab tab);
    /// <summary>Adds a quick-settings tile to the Controls tab ("Mods" row).</summary>
    void AddTile(ModTile tile);
    /// <summary>Opens the panel on one of this mod's tabs.</summary>
    void OpenTab(string tabId);
    /// <summary>Shows a short heads-up message in the notch.</summary>
    void ShowHud(string text, TimeSpan? duration = null);
    /// <summary>Simple per-mod key/value settings persisted as JSON in <see cref="DataDirectory"/>.</summary>
    T GetSetting<T>(string key, T fallback);
    void SetSetting<T>(string key, T value);
    void Log(string message);
}

/// <summary>A tab contributed by a mod. The view may implement <see cref="ITabView"/> to start/stop work when shown/hidden.</summary>
public sealed class ModTab
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>Material Symbols name (fonts.google.com/icons), e.g. "extension".</summary>
    public string Icon { get; init; } = "extension";
    /// <summary>Creates the view (called lazily, once). Width available: 792 DIP; keep height ≤ ~520.</summary>
    public required Func<FrameworkElement> CreateView { get; init; }
    /// <summary>Set by the registry.</summary>
    public string ModId { get; internal set; } = "";
}

/// <summary>A quick-settings tile contributed by a mod. Properties are observable — update them any time (on the UI thread).</summary>
public sealed partial class ModTile : ObservableObject
{
    public required string Id { get; init; }
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _subtitle;
    [ObservableProperty] private string _icon = "extension";
    [ObservableProperty] private bool _isActive;
    /// <summary>Tile click.</summary>
    public Action? OnClick { get; init; }
    /// <summary>Chevron click; when null the tile has no chevron.</summary>
    public Action? OnChevron { get; init; }
    public string ModId { get; internal set; } = "";
}

public enum ModKind { Widget, Assembly }

/// <summary>An installed mod as shown in Settings → Mods.</summary>
public sealed record ModInfo(
    string Id, string Name, string? Version, string? Author, string? Description, ModKind Kind,
    string Directory, bool IsEnabled, string? Error);

/// <summary>Everything mods registered (UI thread only). The shell and the Controls tab observe these collections.</summary>
public static class ModRegistry
{
    public static ObservableCollection<ModTab> Tabs { get; } = [];
    public static ObservableCollection<ModTile> Tiles { get; } = [];
    public static ObservableCollection<ModInfo> Mods { get; } = [];

    /// <summary>A mod asked to open one of its tabs (the shell expands and selects it).</summary>
    public static event EventHandler<string>? TabOpenRequested;

    internal static void RequestOpenTab(string tabId) => TabOpenRequested?.Invoke(null, tabId);
}
