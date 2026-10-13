namespace NotchHub.Core.Settings;

/// <summary>
/// All user settings, persisted to %APPDATA%\NotchHub\settings.json.
/// Mutate via <see cref="ISettingsService.Update"/> so the change is saved and broadcast.
/// Adding properties is backward compatible (missing JSON members keep their defaults).
/// </summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>"pl", "en" or "auto" (follow Windows UI language).</summary>
    public string Language { get; set; } = "pl";

    public AppearanceSettings Appearance { get; set; } = new();
    public NotchSettings Notch { get; set; } = new();
    public ShortcutSettings Shortcuts { get; set; } = new();
    public ClipboardSettings Clipboard { get; set; } = new();
    public WeatherSettings Weather { get; set; } = new();
    public CalendarSettings Calendar { get; set; } = new();
    public PhoneSettings Phone { get; set; } = new();
    public StatsSettings Stats { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public ModSettings Mods { get; set; } = new();
    public MeetingBarSettings MeetingBar { get; set; } = new();
    public MediaSettings Media { get; set; } = new();
}

public sealed class MediaSettings
{
    /// <summary>Client id of the user's own Spotify app (developer.spotify.com); empty = Spotify integration off.</summary>
    public string? SpotifyClientId { get; set; }
}

/// <summary>"Meeting bar" (like MeetingBar on macOS): live countdown of the next / current calendar event in the idle notch.</summary>
public sealed class MeetingBarSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Start showing an upcoming event this many minutes before it starts.</summary>
    public int LeadMinutes { get; set; } = 60;
    /// <summary>While an event is running show the time left until it ends.</summary>
    public bool ShowOngoing { get; set; } = true;
    /// <summary>Show it next to the media live activity (otherwise media wins).</summary>
    public bool ShowWithMedia { get; set; } = true;
    /// <summary>Ignore all-day events.</summary>
    public bool SkipAllDay { get; set; } = true;
    /// <summary>Calendar source ids excluded from the bar (empty = all calendars).</summary>
    public List<string> ExcludedSources { get; set; } = new();
    /// <summary>Longest title shown before it is ellipsized (the notch widens up to this).</summary>
    public int MaxTitleChars { get; set; } = 28;
}

public enum ColorSource
{
    /// <summary>Material You: most dominant/suitable color of the wallpaper (quantize + score).</summary>
    WallpaperDominant,
    /// <summary>Average color of the wallpaper ("as if blurred to a single color").</summary>
    WallpaperAverage,
    /// <summary>Windows accent color.</summary>
    WindowsAccent,
    /// <summary><see cref="AppearanceSettings.CustomColor"/>.</summary>
    Custom,
}

/// <summary>Material dynamic color scheme variants (same names as Android / material-color-utilities).</summary>
public enum SchemeVariant { TonalSpot, Vibrant, Expressive, Fidelity, Content, Neutral, Monochrome, Rainbow, FruitSalad }

/// <summary>Background of the panel: pure black (looks like a hardware notch) or a dark surface tinted with the theme color.</summary>
public enum PanelStyle { Black, Tinted }

public sealed class AppearanceSettings
{
    public ColorSource Source { get; set; } = ColorSource.WallpaperDominant;
    /// <summary>Which of the wallpaper candidate colors to use (0 = best scored). Only for <see cref="ColorSource.WallpaperDominant"/>.</summary>
    public int WallpaperCandidateIndex { get; set; }
    public string CustomColor { get; set; } = "#D4AF5A";
    public SchemeVariant Variant { get; set; } = SchemeVariant.TonalSpot;
    public PanelStyle PanelStyle { get; set; } = PanelStyle.Black;
    /// <summary>-1 (reduced) .. 0 (standard) .. 1 (high) contrast.</summary>
    public double Contrast { get; set; }
    public bool ReduceMotion { get; set; }
    /// <summary>Legacy = original storyboard springs; Refreshed = physics springs with velocity continuity (docs/MOTION.md).</summary>
    public AnimationStyle AnimationStyle { get; set; } = AnimationStyle.Refreshed;
}

/// <summary>Legacy = original; Refreshed = physics springs; Smooth = Refreshed + motion blur on fast movement (best on high refresh monitors).</summary>
public enum AnimationStyle { Legacy, Refreshed, Smooth }

public sealed class ModSettings
{
    /// <summary>Ids of installed mods the user switched off.</summary>
    public List<string> Disabled { get; set; } = new();
    /// <summary>Allow no-code widget mods to run their "command" data source (a local script/program).</summary>
    public bool AllowCommands { get; set; } = true;
}

public enum HoverBehavior
{
    /// <summary>Hover shows the compact peek (mini player / glance); click expands the full panel.</summary>
    PeekThenClick,
    /// <summary>Hover expands the full panel directly.</summary>
    ExpandOnHover,
    /// <summary>Only clicks (and hotkeys) open the panel.</summary>
    ClickOnly,
}

/// <summary>Height of the reserved top strip when <see cref="NotchSettings.ReserveScreenSpace"/> is on.</summary>
public enum ReservedSpaceMode
{
    /// <summary>Height of the plain collapsed notch.</summary>
    Notch,
    /// <summary>Height of the collapsed notch showing a live activity (album art + equalizer), so it never covers windows.</summary>
    LiveActivity,
    /// <summary>Follows the idle notch: plain-notch height while it is empty, live-activity height while it shows something
    /// (media, meeting bar, call…) — maximized windows are re-fitted automatically.</summary>
    Dynamic,
}

public sealed class NotchSettings
{
    /// <summary>Index into the monitor list; -1 = primary monitor.</summary>
    public int MonitorIndex { get; set; } = -1;
    public double CollapsedWidth { get; set; } = 200;
    public double CollapsedHeight { get; set; } = 12;
    public HoverBehavior Hover { get; set; } = HoverBehavior.PeekThenClick;
    public int HoverDelayMs { get; set; } = 120;
    public int CollapseDelayMs { get; set; } = 450;
    /// <summary>Show album art + equalizer bars around the collapsed notch while media plays.</summary>
    public bool ShowLiveActivity { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Show a volume/brightness HUD in the notch when the value changes (e.g. media keys).</summary>
    public bool ShowHud { get; set; } = true;
    /// <summary>Scroll the mouse wheel down over the notch to expand it.</summary>
    public bool ScrollToExpand { get; set; } = true;
    /// <summary>
    /// Reserve a strip at the top of the notch monitor (Windows AppBar) so maximized windows end below the collapsed notch
    /// instead of sliding underneath it. Height per <see cref="ReservedSpace"/>.
    /// </summary>
    public bool ReserveScreenSpace { get; set; }
    public ReservedSpaceMode ReservedSpace { get; set; } = ReservedSpaceMode.Notch;
    /// <summary>Hover peek shows the glance card (time, weather, next event, phone) next to the mini player instead of only the player.</summary>
    public bool PeekGlanceWithMedia { get; set; } = true;
    public string DefaultTab { get; set; } = "Controls";
    public bool RememberLastTab { get; set; } = true;
    public string? LastTab { get; set; }
}

public sealed class ShortcutSettings
{
    /// <summary>Global hotkey toggling the panel. Format: "Ctrl+Alt+Space". Empty = disabled.</summary>
    public string TogglePanel { get; set; } = "Ctrl+Alt+Space";
    /// <summary>Global hotkey opening the clipboard tab.</summary>
    public string OpenClipboard { get; set; } = "Ctrl+Alt+V";
}

public sealed class ClipboardSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxItems { get; set; } = 300;
    public bool SaveImages { get; set; } = true;
    /// <summary>Skip content flagged by password managers (ExcludeClipboardContentFromMonitorProcessing etc.).</summary>
    public bool IgnoreSensitive { get; set; } = true;
    /// <summary>Executable names (e.g. "KeePassXC.exe") whose copies are ignored.</summary>
    public List<string> IgnoredApps { get; set; } = new();
    public int RetentionDays { get; set; } = 30;
}

public enum TemperatureUnit { Celsius, Fahrenheit }

public sealed class WeatherSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>City name typed by the user; null/empty = use IP based location.</summary>
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>Resolved display name of the location.</summary>
    public string? LocationName { get; set; }
    public TemperatureUnit Unit { get; set; } = TemperatureUnit.Celsius;
}

public sealed class CalendarSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string ColorHex { get; set; } = "#F28FAD";
    public bool Enabled { get; set; } = true;
}

public sealed class CalendarSettings
{
    public List<CalendarSubscription> Subscriptions { get; set; } = new();
    public int RefreshMinutes { get; set; } = 15;
    public bool WeekStartsMonday { get; set; } = true;
    /// <summary>OAuth client id ("Desktop app" type) created by the user in Google Cloud Console; empty = Google integration off.</summary>
    public string? GoogleClientId { get; set; }
    /// <summary>Client secret of that desktop client (Google requires it for installed apps; not confidential).</summary>
    public string? GoogleClientSecret { get; set; }
    /// <summary>Google calendar ids hidden by the user.</summary>
    public List<string> GoogleHiddenCalendars { get; set; } = new();
    /// <summary>Where new events go: "local" or a Google calendar id.</summary>
    public string DefaultEventTarget { get; set; } = "local";
}

public sealed class PhoneSettings
{
    /// <summary>Enables the built-in KDE Connect compatible endpoint.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Name announced to phones. Null = machine name.</summary>
    public string? DeviceName { get; set; }
    public bool ShowStatusInHeader { get; set; } = true;
    /// <summary>Show a notch peek when the phone posts a new notification.</summary>
    public bool NotificationPeeks { get; set; } = true;
    /// <summary>Put text received from the phone on the PC clipboard and into history.</summary>
    public bool ReceiveClipboard { get; set; } = true;
    /// <summary>App names whose notifications never peek.</summary>
    public List<string> MutedApps { get; set; } = new();
    /// <summary>Brief notch HUD when the phone starts / stops charging.</summary>
    public bool ChargingHud { get; set; } = true;
    /// <summary>Show incoming / ongoing phone calls in the notch.</summary>
    public bool CallStatus { get; set; } = true;
    /// <summary>Detect one-time codes in notifications and offer one-click copy.</summary>
    public bool OtpCopy { get; set; } = true;
}

public sealed class StatsSettings
{
    /// <summary>Drive shown in the disk card, e.g. "C:\".</summary>
    public string Disk { get; set; } = "C:\\";
    /// <summary>"ram" or "cpu" — sorting of the top apps list.</summary>
    public string TopAppsSort { get; set; } = "ram";
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool FirstRunCompleted { get; set; }
}
