using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;

namespace NotchHub.UI.Modding;

/// <summary>mod.json (see docs/MODDING.md).</summary>
public sealed class ModManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    /// <summary>"widget" (XAML + JSON data, no compiling) or "assembly" (C# DLL implementing INotchMod).</summary>
    public string Kind { get; set; } = "widget";
    /// <summary>assembly mods: DLL file name relative to the mod folder.</summary>
    public string? Assembly { get; set; }
    /// <summary>widget mods: the XAML view file (default "view.xaml").</summary>
    public string View { get; set; } = "view.xaml";
    public ManifestTab? Tab { get; set; }
    public ManifestTile? Tile { get; set; }
    public ManifestData? Data { get; set; }
}

public sealed class ManifestTab
{
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "extension";
}

/// <summary>Tile texts may contain {path} placeholders resolved against the widget data, e.g. "{rates.0.mid} zł".</summary>
public sealed class ManifestTile
{
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string Icon { get; set; } = "extension";
}

public sealed class ManifestData
{
    /// <summary>HTTP(S) URL returning JSON.</summary>
    public string? Url { get; set; }
    /// <summary>Program run in the mod folder whose standard output is JSON (e.g. "python"); needs Settings → Mods → allow commands.</summary>
    public string? Command { get; set; }
    public List<string> Args { get; set; } = [];
    public int IntervalSeconds { get; set; } = 300;
    /// <summary>Optional HTTP headers (e.g. Accept).</summary>
    public Dictionary<string, string> Headers { get; set; } = [];
}

/// <summary>
/// Discovers and loads mods from %APPDATA%\NotchHub\mods and &lt;app folder&gt;\mods (one sub folder per mod with a mod.json).
/// Must be called on the UI thread after the theme resources are loaded. Failures never stop the app: they are shown in
/// Settings → Mods (ModInfo.Error).
/// </summary>
public static class ModLoader
{
    private static readonly List<(INotchMod Mod, string Id)> Loaded = [];
    private static readonly List<WidgetMod> Widgets = [];

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Folders searched for mods.</summary>
    public static IReadOnlyList<string> ModFolders =>
    [
        AppPaths.Dir("mods"),
        Path.Combine(AppContext.BaseDirectory, "mods"),
    ];

    public static void LoadAll(ISettingsService settings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ModFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(dir, "mod.json");
                if (!File.Exists(manifestPath)) continue;
                ModManifest? m = null;
                try
                {
                    m = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(manifestPath), JsonOptions);
                    if (m is null || string.IsNullOrWhiteSpace(m.Id)) throw new InvalidDataException("mod.json: \"id\" is required");
                    if (!seen.Add(m.Id)) continue; // %APPDATA% copy wins over the app folder copy
                    var enabled = !settings.Current.Mods.Disabled.Contains(m.Id, StringComparer.OrdinalIgnoreCase);
                    if (enabled) Load(m, dir, settings);
                    Add(m, dir, enabled, null);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Mods: failed to load {dir}", ex);
                    Add(m ?? new ModManifest { Id = Path.GetFileName(dir), Name = Path.GetFileName(dir) }, dir, true, ex.Message);
                }
            }
        }
        Log.Info($"Mods: {ModRegistry.Mods.Count} found, {Loaded.Count + Widgets.Count} running");
    }

    public static void ShutdownAll()
    {
        foreach (var (mod, id) in Loaded) Log.Safe($"Mod {id} shutdown", mod.Shutdown);
        foreach (var w in Widgets) w.Dispose();
    }

    private static void Add(ModManifest m, string dir, bool enabled, string? error) =>
        ModRegistry.Mods.Add(new ModInfo(m.Id, string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name, m.Version, m.Author, m.Description,
            string.Equals(m.Kind, "assembly", StringComparison.OrdinalIgnoreCase) ? ModKind.Assembly : ModKind.Widget, dir, enabled, error));

    private static void Load(ModManifest m, string dir, ISettingsService settings)
    {
        var context = new ModContext(m.Id, dir);
        if (string.Equals(m.Kind, "assembly", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(m.Assembly)) throw new InvalidDataException("mod.json: \"assembly\" is required for assembly mods");
            var asm = System.Reflection.Assembly.LoadFrom(Path.Combine(dir, m.Assembly));
            var types = asm.GetExportedTypes().Where(t => typeof(INotchMod).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }).ToList();
            if (types.Count == 0) throw new InvalidDataException($"{m.Assembly} contains no public INotchMod class");
            foreach (var t in types)
            {
                var mod = (INotchMod)Activator.CreateInstance(t)!;
                mod.Initialize(context);
                Loaded.Add((mod, m.Id));
            }
        }
        else
        {
            Widgets.Add(new WidgetMod(m, dir, context, settings));
        }
    }
}

internal sealed class ModContext(string id, string dir) : IModContext
{
    private readonly object _gate = new();
    private Dictionary<string, JsonElement>? _settings;

    public string ModId { get; } = id;
    public string ModDirectory { get; } = dir;
    public string DataDirectory { get; } = AppPaths.Dir(Path.Combine("mods-data", id));

    public void AddTab(ModTab tab)
    {
        tab.ModId = ModId;
        ModRegistry.Tabs.Add(tab);
    }

    public void AddTile(ModTile tile)
    {
        tile.ModId = ModId;
        ModRegistry.Tiles.Add(tile);
    }

    public void OpenTab(string tabId) => ModRegistry.RequestOpenTab(tabId);

    public void ShowHud(string text, TimeSpan? duration = null) =>
        AppServices.Shell?.ShowHud(new HudRequest(HudKind.Info, text, duration));

    public T GetSetting<T>(string key, T fallback)
    {
        lock (_gate)
        {
            LoadSettings();
            try { return _settings!.TryGetValue(key, out var v) ? v.Deserialize<T>() ?? fallback : fallback; }
            catch { return fallback; }
        }
    }

    public void SetSetting<T>(string key, T value)
    {
        lock (_gate)
        {
            LoadSettings();
            _settings![key] = JsonSerializer.SerializeToElement(value);
            JsonStore.Save(Path.Combine(DataDirectory, "settings.json"), _settings);
        }
    }

    public void Log(string message) => NotchHub.Core.Log.Info($"[mod:{ModId}] {message}");

    private void LoadSettings() =>
        _settings ??= JsonStore.Load(Path.Combine(DataDirectory, "settings.json"), () => new Dictionary<string, JsonElement>());
}
