using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Settings;
using NotchHub.UI.Localization;
using NotchHub.UI.Modding;

namespace NotchHub.Features.Settings;

/// <summary>Settings → Mods (installed mods, enable switches, commands permission) + Appearance → animation style.</summary>
public sealed partial class SettingsViewModel
{
    // ═════════════ Wygląd → Animacje ═════════════

    [ObservableProperty] private ObservableCollection<Choice> _animationStyles = [];

    private void LoadAnimationStyles() =>
        AnimationStyles = Choices.Build(
        [
            (Loc.Get("settings.Appearance.AnimLegacy"), AnimationStyle.Legacy),
            (Loc.Get("settings.Appearance.AnimRefreshed"), AnimationStyle.Refreshed),
            (Loc.Get("settings.Appearance.AnimSmooth"), AnimationStyle.Smooth),
        ], S.Appearance.AnimationStyle, v => Save(s => s.Appearance.AnimationStyle = (AnimationStyle)v));

    // ═════════════ Mody ═════════════

    public ObservableCollection<ModItem> Mods { get; } = [];
    [ObservableProperty] private bool _allowCommands;
    [ObservableProperty] private bool _modsRestartNeeded;
    [ObservableProperty] private bool _showModGuide;
    private bool _modsHooked;

    public string ModsFolder => SafeModsFolder();

    private static string SafeModsFolder()
    {
        try { return AppPaths.Dir("mods"); }
        catch { return Path.Combine(AppPaths.Root, "mods"); }
    }

    private void LoadMods()
    {
        if (!_modsHooked)
        {
            _modsHooked = true;
            // the registry lives for the whole app; weak-ish coupling is fine (one Settings view per app)
            ModRegistry.Mods.CollectionChanged += OnRegistryModsChanged;
        }
        AllowCommands = S.Mods.AllowCommands;
        Mods.Clear();
        var disabled = S.Mods.Disabled;
        foreach (var info in ModRegistry.Mods.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var enabled = !disabled.Contains(info.Id, StringComparer.OrdinalIgnoreCase);
            Mods.Add(new ModItem(info, enabled, OnModToggled));
        }
        UpdateRestartHint();
    }

    private void OnRegistryModsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        AppServices.Ui.Post(() =>
        {
            _loading = true;
            try { LoadMods(); }
            finally { _loading = false; }
        });

    private void OnModToggled(ModItem item, bool enabled)
    {
        Save(s =>
        {
            s.Mods.Disabled.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (!enabled) s.Mods.Disabled.Add(item.Id);
        });
        UpdateRestartHint();
    }

    private void UpdateRestartHint() => ModsRestartNeeded = Mods.Any(m => m.IsEnabled != m.Info.IsEnabled);

    partial void OnAllowCommandsChanged(bool value) => Save(s => s.Mods.AllowCommands = value);

    [RelayCommand] private static void OpenModsFolder() => OpenFolder(SafeModsFolder());

    /// <summary>Opens MODDING.md shipped next to the exe (or in docs\), otherwise toggles the short inline guide.</summary>
    [RelayCommand]
    private void OpenModGuide()
    {
        var baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "docs", "MODDING.md"),
            Path.Combine(baseDir, "MODDING.md"),
        ];
        var file = candidates.FirstOrDefault(File.Exists);
        if (file is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                return;
            }
            catch (Exception ex) { Log.Warn("Settings: open MODDING.md failed", ex); }
        }
        ShowModGuide = !ShowModGuide;
    }
}

/// <summary>One installed mod row in Settings → Mods.</summary>
public sealed partial class ModItem : ObservableObject
{
    private readonly Action<ModItem, bool> _toggled;
    private bool _init;

    public ModItem(ModInfo info, bool enabled, Action<ModItem, bool> toggled)
    {
        Info = info;
        _toggled = toggled;
        _isEnabled = enabled;
        _init = true;
    }

    public ModInfo Info { get; }
    public string Id => Info.Id;
    public string Name => Info.Name;
    public string? Description => string.IsNullOrWhiteSpace(Info.Description) ? null : Info.Description;
    public string? Error => string.IsNullOrWhiteSpace(Info.Error) ? null : Info.Error;
    public bool HasError => Error is not null;
    /// <summary>Whether the mod's code is running in this session (i.e. it was enabled at startup and loaded without error).</summary>
    public bool WasRunning => Info.IsEnabled && !HasError;
    public string Icon => Info.Kind == ModKind.Assembly ? "code" : "widgets";
    public string KindLabel => Loc.Get(Info.Kind == ModKind.Assembly ? "settings.Mods.KindAssembly" : "settings.Mods.KindWidget");

    /// <summary>"1.0.0 · Autor" (empty parts skipped).</summary>
    public string Meta => string.Join(" · ", new[]
    {
        string.IsNullOrWhiteSpace(Info.Version) ? null : "v" + Info.Version.TrimStart('v', 'V'),
        string.IsNullOrWhiteSpace(Info.Author) ? null : Info.Author,
        Info.Id,
    }.Where(s => s is not null));

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_init) _toggled(this, value);
    }
}
