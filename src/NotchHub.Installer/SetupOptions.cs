using System.ComponentModel;
using System.Runtime.CompilerServices;
using NotchHub.Core.Settings;
using NotchHub.Installer.Engine;

namespace NotchHub.Installer;

public enum ReserveChoice { Off, Notch, Dynamic }

/// <summary>Choices on the "Opcje" page. App settings are written only on a fresh install (no settings.json yet).</summary>
public sealed class SetupOptions : INotifyPropertyChanged
{
    public static readonly string[] Swatches = ["#D4AF5A", "#8AB4F8", "#7FD8BE", "#F28FAD", "#C3A6FF", "#FFB870"];

    private string _language = "pl";
    private ColorSource _source = ColorSource.WallpaperDominant;
    private string _customColor = "#D4AF5A";
    private SchemeVariant _variant = SchemeVariant.TonalSpot;
    private AnimationStyle _animation = AnimationStyle.Refreshed;
    private bool _startWithWindows = true;
    private HoverBehavior _hover = HoverBehavior.PeekThenClick;
    private ReserveChoice _reserve = ReserveChoice.Off;
    private bool _phone = true, _clipboard = true, _meetingBar = true, _hotkeys = true;
    private bool _desktopShortcut = true, _startMenuShortcut = true, _launchAfter = true, _exampleMods = true;
    private string _installDir = SetupContext.DefaultInstallDir;

    public string Language { get => _language; set => Set(ref _language, value); }
    public ColorSource Source { get => _source; set { if (Set(ref _source, value)) Raise(nameof(IsCustom)); } }
    public bool IsCustom => _source == ColorSource.Custom;
    public string CustomColor { get => _customColor; set => Set(ref _customColor, value); }
    public SchemeVariant Variant { get => _variant; set => Set(ref _variant, value); }
    public AnimationStyle Animation { get => _animation; set => Set(ref _animation, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public HoverBehavior Hover { get => _hover; set => Set(ref _hover, value); }
    public ReserveChoice Reserve { get => _reserve; set => Set(ref _reserve, value); }
    public bool Phone { get => _phone; set => Set(ref _phone, value); }
    public bool Clipboard { get => _clipboard; set => Set(ref _clipboard, value); }
    public bool MeetingBar { get => _meetingBar; set => Set(ref _meetingBar, value); }
    /// <summary>Global hotkeys (Ctrl+Alt+Space panel, Ctrl+Alt+V clipboard); off = written as empty (disabled).</summary>
    public bool Hotkeys { get => _hotkeys; set => Set(ref _hotkeys, value); }
    public bool DesktopShortcut { get => _desktopShortcut; set => Set(ref _desktopShortcut, value); }
    public bool StartMenuShortcut { get => _startMenuShortcut; set => Set(ref _startMenuShortcut, value); }
    public bool LaunchAfter { get => _launchAfter; set => Set(ref _launchAfter, value); }
    public bool ExampleMods { get => _exampleMods; set => Set(ref _exampleMods, value); }
    public string InstallDir { get => _installDir; set => Set(ref _installDir, value); }

    /// <summary>Settings of a fresh install (defaults of <see cref="AppSettings"/> + the choices made here).</summary>
    public AppSettings ToSettings()
    {
        var s = new AppSettings { Language = Language };
        s.Appearance.Source = Source;
        s.Appearance.CustomColor = CustomColor;
        s.Appearance.Variant = Variant;
        s.Appearance.AnimationStyle = Animation;
        s.General.StartWithWindows = StartWithWindows;
        s.Notch.Hover = Hover;
        s.Notch.ReserveScreenSpace = Reserve != ReserveChoice.Off;
        s.Notch.ReservedSpace = Reserve == ReserveChoice.Dynamic ? ReservedSpaceMode.Dynamic : ReservedSpaceMode.Notch;
        s.Phone.Enabled = Phone;
        s.Clipboard.Enabled = Clipboard;
        s.MeetingBar.Enabled = MeetingBar;
        if (!Hotkeys)
        {
            s.Shortcuts.TogglePanel = "";
            s.Shortcuts.OpenClipboard = "";
        }
        return s;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
