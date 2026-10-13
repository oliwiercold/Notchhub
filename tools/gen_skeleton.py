# One-off generator for the initial skeleton (projects + compiling stubs). Not used after bootstrap.
import os, textwrap
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src")

def w(rel, content):
    p = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(textwrap.dedent(content).lstrip("\n"))

def csproj(name, tfm="$(WindowsTfm)", wpf=False, refs=(), pkgs=(), extra="", sdk="Microsoft.NET.Sdk", output=None):
    lines = [f'<Project Sdk="{sdk}">', "  <PropertyGroup>", f"    <TargetFramework>{tfm}</TargetFramework>", f"    <RootNamespace>{name}</RootNamespace>"]
    if wpf: lines.append("    <UseWPF>true</UseWPF>")
    if tfm != "net10.0": lines.append("    <SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>")
    if output: lines.append(f"    <OutputType>{output}</OutputType>")
    lines.append("  </PropertyGroup>")
    if refs:
        lines.append("  <ItemGroup>")
        for r in refs: lines.append(f'    <ProjectReference Include="..\\{r}\\{r}.csproj" />')
        lines.append("  </ItemGroup>")
    if pkgs:
        lines.append("  <ItemGroup>")
        for p, v in pkgs: lines.append(f'    <PackageReference Include="{p}" Version="{v}" />')
        lines.append("  </ItemGroup>")
    if extra: lines.append(extra.rstrip())
    lines.append("</Project>")
    w(f"{name}/{name}.csproj", "\n".join(lines) + "\n")

STUB_NOTE = "// SKELETON STUB — replace with the real implementation (keep class name, namespace and constructor signature).\n"

# ───────── Platform.Media
csproj("NotchHub.Platform.Media", refs=["NotchHub.Core"], pkgs=[("NAudio.Wasapi", "2.2.1")])
w("NotchHub.Platform.Media/Media/MediaService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Media.Media;

public sealed class MediaService : IMediaService
{
    public MediaSessionInfo? Current => null;
    public IReadOnlyList<MediaSessionInfo> Sessions => [];
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PlayPauseAsync() => Task.CompletedTask;
    public Task PlayAsync() => Task.CompletedTask;
    public Task PauseAsync() => Task.CompletedTask;
    public Task NextAsync() => Task.CompletedTask;
    public Task PreviousAsync() => Task.CompletedTask;
    public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    public Task SetShuffleAsync(bool enabled) => Task.CompletedTask;
    public Task SetRepeatAsync(MediaRepeatMode mode) => Task.CompletedTask;
    public void SelectSession(string? sessionId) { }
    public void Dispose() { }
}
""")
w("NotchHub.Platform.Media/Audio/AudioService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Media.Audio;

public sealed class AudioService : IAudioService
{
    public bool IsAvailable => false;
    public float Volume => 0;
    public bool IsMuted => false;
    public AudioDevice? DefaultOutput => null;
    public IReadOnlyList<AudioDevice> Outputs => [];
    public IReadOnlyList<AudioAppSession> Sessions => [];
    public event EventHandler? Changed { add { } remove { } }
    public event EventHandler<AudioVolumeChangedEventArgs>? VolumeChanged { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SetVolume(float volume) { }
    public void SetMuted(bool muted) { }
    public Task<bool> SetDefaultOutputAsync(string deviceId) => Task.FromResult(false);
    public void SetSessionVolume(string sessionId, float volume) { }
    public void SetSessionMuted(string sessionId, bool muted) { }
    public void OpenSettings() { }
    public void Dispose() { }
}
""")

# ───────── Platform.Connectivity
csproj("NotchHub.Platform.Connectivity", refs=["NotchHub.Core"])
w("NotchHub.Platform.Connectivity/Network/NetworkService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Connectivity.Network;

public sealed class NetworkService : INetworkService
{
    public NetworkStatus Status { get; } = new();
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> SetWifiEnabledAsync(bool enabled) => Task.FromResult(false);
    public Task<IReadOnlyList<WifiNetwork>> ScanWifiAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WifiNetwork>>([]);
    public Task<bool> ConnectWifiAsync(string ssid) => Task.FromResult(false);
    public Task<bool> DisconnectWifiAsync() => Task.FromResult(false);
    public void OpenSettings() { }
    public void Dispose() { }
}
""")
w("NotchHub.Platform.Connectivity/Bluetooth/BluetoothService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Connectivity.Bluetooth;

public sealed class BluetoothService : IBluetoothService
{
    public bool IsAvailable => false;
    public bool IsEnabled => false;
    public bool CanToggle => false;
    public IReadOnlyList<BluetoothDeviceInfo> Devices => [];
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> SetEnabledAsync(bool enabled) => Task.FromResult(false);
    public Task<bool> ConnectAsync(string deviceId) => Task.FromResult(false);
    public Task<bool> DisconnectAsync(string deviceId) => Task.FromResult(false);
    public void OpenSettings() { }
    public void Dispose() { }
}
""")
w("NotchHub.Platform.Connectivity/Display/DisplayService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Connectivity.Display;

public sealed class DisplayService : IDisplayService
{
    public bool NightLightSupported => false;
    public bool NightLightEnabled => false;
    public bool DarkModeEnabled => true;
    public bool BrightnessSupported => false;
    public int Brightness => 0;
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> SetNightLightAsync(bool enabled) => Task.FromResult(false);
    public Task<bool> SetDarkModeAsync(bool enabled) => Task.FromResult(false);
    public Task<bool> SetBrightnessAsync(int percent) => Task.FromResult(false);
    public void OpenNightLightSettings() { }
    public void OpenDisplaySettings() { }
    public void Dispose() { }
}
""")

# ───────── Platform.Machine
csproj("NotchHub.Platform.Machine", refs=["NotchHub.Core"])
w("NotchHub.Platform.Machine/Stats/StatsService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;

namespace NotchHub.Platform.Machine.Stats;

public sealed class StatsService(ISettingsService settings) : IStatsService
{
    private readonly ISettingsService _settings = settings;
    public SystemStats? Latest => null;
    public event EventHandler? Updated { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public IDisposable RequestLiveUpdates() => new NoopToken();
    public void Dispose() { }
    private sealed class NoopToken : IDisposable { public void Dispose() { } }
}
""")
w("NotchHub.Platform.Machine/SpeedTest/SpeedTestService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Machine.SpeedTest;

public sealed class SpeedTestService : ISpeedTestService
{
    public SpeedTestPhase Phase => SpeedTestPhase.Idle;
    public bool IsRunning => false;
    public double Progress => 0;
    public double CurrentMbps => 0;
    public SpeedTestResult? LastResult => null;
    public string? LastError => null;
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RunAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Cancel() { }
    public void Dispose() { }
}
""")
w("NotchHub.Platform.Machine/Power/PowerService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Machine.Power;

public sealed class PowerService : IPowerService
{
    public bool IsSupported(PowerAction action) => false;
    public Task<bool> ExecuteAsync(PowerAction action) => Task.FromResult(false);
}
""")

# ───────── Platform.Shell
csproj("NotchHub.Platform.Shell", wpf=True, refs=["NotchHub.Core"])
w("NotchHub.Platform.Shell/Clipboard/ClipboardService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;

namespace NotchHub.Platform.Shell.Clipboard;

public sealed class ClipboardService(ISettingsService settings) : IClipboardService
{
    private readonly ISettingsService _settings = settings;
    public IReadOnlyList<ClipItem> Items => [];
    public bool IsMonitoring => false;
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SetPinned(Guid id, bool pinned) { }
    public void Delete(Guid id) { }
    public void Delete(IReadOnlyList<Guid> ids) { }
    public void Clear(bool keepPinned = true) { }
    public Task<bool> CopyToClipboardAsync(IReadOnlyList<Guid> ids) => Task.FromResult(false);
    public Task<bool> PasteAsync(IReadOnlyList<Guid> ids) => Task.FromResult(false);
    public void AddExternal(string text, ClipOrigin origin, bool setSystemClipboard) { }
    public Task<string?> GetSystemClipboardTextAsync() => Task.FromResult<string?>(null);
    public void Dispose() { }
}
""")
w("NotchHub.Platform.Shell/Foreground/ForegroundService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Shell.Foreground;

public sealed class ForegroundService : IForegroundService
{
    public bool IsFullscreenAppActive => false;
    public event EventHandler? FullscreenChanged { add { } remove { } }
    public string? LastExternalAppName => null;
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SetOwnWindow(IntPtr hwnd) { }
    public bool RestorePreviousForeground() => false;
    public void SendPasteKeystroke() { }
    public void Dispose() { }
}
""")
w("NotchHub.Platform.Shell/Hotkeys/HotkeyService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Shell.Hotkeys;

public sealed class HotkeyService : IHotkeyService
{
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public bool Register(string id, string gesture, Action callback) => false;
    public void Unregister(string id) { }
    public bool IsValidGesture(string gesture) => false;
    public void Dispose() { }
}
""")
w("NotchHub.Platform.Shell/Wallpaper/WallpaperService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Shell.Wallpaper;

public sealed class WallpaperService : IWallpaperService
{
    public string? CurrentPath => null;
    public uint? SolidColor => null;
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<WallpaperPixels?> GetPixelsAsync(int maxSize = 128, CancellationToken ct = default) => Task.FromResult<WallpaperPixels?>(null);
    public void Dispose() { }
}
""")
w("NotchHub.Platform.Shell/Autostart/AutostartService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Shell.Autostart;

public sealed class AutostartService : IAutostartService
{
    public bool IsEnabled => false;
    public bool SetEnabled(bool enabled) => false;
}
""")

# ───────── Services (pure .NET)
csproj("NotchHub.Services", tfm="net10.0", refs=["NotchHub.Core"], pkgs=[("Ical.Net", "5.2.3")])
w("NotchHub.Services/Weather/WeatherService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;

namespace NotchHub.Services.Weather;

public sealed class WeatherService(ISettingsService settings) : IWeatherService
{
    private readonly ISettingsService _settings = settings;
    public WeatherSnapshot? Current => null;
    public bool IsLoading => false;
    public string? LastError => null;
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<GeoLocation>> SearchLocationAsync(string query, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GeoLocation>>([]);
    public void Dispose() { }
}
""")
w("NotchHub.Services/Calendar/CalendarService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;

namespace NotchHub.Services.Calendar;

public sealed class CalendarService(ISettingsService settings) : ICalendarService
{
    private readonly ISettingsService _settings = settings;
    public IReadOnlyList<CalendarSourceInfo> Sources => [];
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public IReadOnlyList<CalendarEvent> GetEvents(DateTimeOffset from, DateTimeOffset to) => [];
    public CalendarEvent? GetNextEvent(DateTimeOffset now) => null;
    public CalendarEvent AddLocalEvent(string title, DateTimeOffset start, DateTimeOffset end, bool isAllDay, string? colorHex = null)
        => new(Guid.NewGuid().ToString("N"), title, start, end, isAllDay, null, null, "local", colorHex);
    public void RemoveLocalEvent(string id) { }
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Dispose() { }
}
""")
w("NotchHub.Services/Todos/TodoService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Services.Todos;

public sealed class TodoService : ITodoService
{
    public IReadOnlyList<TodoItem> Items => [];
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public TodoItem Add(string text) => new(Guid.NewGuid(), text, false, DateTimeOffset.Now, null);
    public void SetDone(Guid id, bool done) { }
    public void Rename(Guid id, string text) { }
    public void Remove(Guid id) { }
    public void Move(Guid id, int newIndex) { }
    public void ClearCompleted() { }
    public void Dispose() { }
}
""")
w("NotchHub.Services/Timer/TimerService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;

namespace NotchHub.Services.Timer;

public sealed class TimerService : ITimerService
{
    public TimerState State => TimerState.Idle;
    public TimeSpan Duration => TimeSpan.Zero;
    public TimeSpan Remaining => TimeSpan.Zero;
    public event EventHandler? Changed { add { } remove { } }
    public event EventHandler? Finished { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Start(TimeSpan duration) { }
    public void Pause() { }
    public void Resume() { }
    public void Reset() { }
    public void AddTime(TimeSpan delta) { }
    public void Dispose() { }
}
""")

# ───────── KDE Connect (pure .NET)
csproj("NotchHub.KdeConnect", tfm="net10.0", refs=["NotchHub.Core"])
w("NotchHub.KdeConnect/PhoneService.cs", STUB_NOTE + """
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;

namespace NotchHub.KdeConnect;

public sealed class PhoneService(ISettingsService settings) : IPhoneService
{
    private readonly ISettingsService _settings = settings;
    public bool IsRunning => false;
    public string LocalDeviceId => "";
    public string LocalDeviceName => Environment.MachineName;
    public IReadOnlyList<PhoneDevice> Devices => [];
    public PhoneDevice? ActiveDevice => null;
    public PhoneBattery? Battery => null;
    public PhoneSignal? Signal => null;
    public IReadOnlyList<PhoneNotification> Notifications => [];
    public event EventHandler? Changed { add { } remove { } }
    public event EventHandler<PhoneNotificationEventArgs>? NotificationPosted { add { } remove { } }
    public event EventHandler<string>? ClipboardReceived { add { } remove { } }
    public event EventHandler<PhoneDevice>? PairRequested { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RefreshAsync() => Task.CompletedTask;
    public Task<bool> RequestPairAsync(string deviceId) => Task.FromResult(false);
    public Task AcceptPairAsync(string deviceId) => Task.CompletedTask;
    public Task RejectPairAsync(string deviceId) => Task.CompletedTask;
    public Task UnpairAsync(string deviceId) => Task.CompletedTask;
    public Task RingAsync() => Task.CompletedTask;
    public Task PingAsync(string? message = null) => Task.CompletedTask;
    public Task SendClipboardAsync(string text) => Task.CompletedTask;
    public Task DismissNotificationAsync(string notificationId) => Task.CompletedTask;
    public Task ReplyToNotificationAsync(string replyId, string message) => Task.CompletedTask;
    public Task InvokeNotificationActionAsync(string notificationId, string action) => Task.CompletedTask;
    public Task RequestNotificationsAsync() => Task.CompletedTask;
    public void Dispose() { }
}
""")

# ───────── Demo fakes
csproj("NotchHub.Demo", tfm="net10.0", refs=["NotchHub.Core"])
w("NotchHub.Demo/DemoServices.cs", STUB_NOTE + """
namespace NotchHub.Demo;

/// <summary>Installs fake implementations of every service into AppServices (used with --demo and for screenshots).</summary>
public static class DemoServices
{
    /// <summary>Sets every AppServices.* service property (except Settings, Ui, Shell) to a fake.</summary>
    public static void Install()
    {
    }
}
""")

# ───────── UI library
csproj("NotchHub.UI", wpf=True, refs=["NotchHub.Core"], extra="""
  <ItemGroup>
    <Resource Include="Assets\\Fonts\\*.ttf" />
    <EmbeddedResource Include="Assets\\Fonts\\MaterialSymbolsRounded.codepoints" LogicalName="NotchHub.UI.MaterialSymbolsRounded.codepoints" />
    <None Include="Assets\\Fonts\\OFL-GoogleSansFlex.txt" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
""")
w("NotchHub.UI/ITabView.cs", """
namespace NotchHub.UI;

/// <summary>Implemented by tab / compact views. The shell calls these when the view becomes visible / hidden,
/// so view models can start/stop live updates (e.g. stats polling, media progress animation).</summary>
public interface ITabView
{
    void OnShown();
    void OnHidden();
}
""")
w("NotchHub.UI/Themes/All.xaml", """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Single entry point merged by App.xaml. Keep the order: tokens → typography → controls. -->
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="pack://application:,,,/NotchHub.UI;component/Themes/Tokens.xaml" />
        <ResourceDictionary Source="pack://application:,,,/NotchHub.UI;component/Themes/Typography.xaml" />
        <ResourceDictionary Source="pack://application:,,,/NotchHub.UI;component/Themes/Controls.xaml" />
    </ResourceDictionary.MergedDictionaries>
</ResourceDictionary>
""")
w("NotchHub.UI/Themes/Typography.xaml", """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- SKELETON: text styles are defined by the design system (see docs/DESIGN.md §Typography) -->
</ResourceDictionary>
""")
w("NotchHub.UI/Themes/Controls.xaml", """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- SKELETON: control styles are defined by the design system (see docs/DESIGN.md §Components) -->
</ResourceDictionary>
""")

FEATURES = {
    "Controls": ["ControlsView"],
    "Today": ["TodayView"],
    "Stats": ["StatsView"],
    "Clipboard": ["ClipboardView"],
    "Phone": ["PhoneView"],
    "Power": ["PowerView"],
    "Settings": ["SettingsView"],
    "Compact": ["LiveActivityView", "PeekView", "HudView"],
}
for feat, views in FEATURES.items():
    proj = f"NotchHub.Features.{feat}"
    csproj(proj, wpf=True, refs=["NotchHub.Core", "NotchHub.UI"])
    for lang, txt in (("pl", f"{feat} (w budowie)"), ("en", f"{feat} (under construction)")):
        w(f"{proj}/Strings/{lang}.xaml", f"""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">
    <!-- Keys MUST be prefixed with "{feat.lower()}." -->
    <sys:String x:Key="{feat.lower()}.placeholder">{txt}</sys:String>
</ResourceDictionary>
""")
    for v in views:
        vm = v.replace("View", "ViewModel")
        w(f"{proj}/{v}.xaml", f"""
<UserControl x:Class="{proj}.{v}"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- SKELETON placeholder -->
    <Border Padding="24" MinWidth="320" MinHeight="64">
        <TextBlock Text="{{DynamicResource {feat.lower()}.placeholder}}" Foreground="White" FontSize="14"
                   HorizontalAlignment="Center" VerticalAlignment="Center" />
    </Border>
</UserControl>
""")
        w(f"{proj}/{v}.xaml.cs", f"""
using System.Windows.Controls;
using NotchHub.UI;

namespace {proj};

public partial class {v} : UserControl, ITabView
{{
    public {v}()
    {{
        InitializeComponent();
        DataContext = new {vm}();
    }}

    public {vm} ViewModel => ({vm})DataContext;

    public void OnShown() => ViewModel.OnShown();
    public void OnHidden() => ViewModel.OnHidden();
}}
""")
        w(f"{proj}/{vm}.cs", f"""
using CommunityToolkit.Mvvm.ComponentModel;

namespace {proj};

public sealed partial class {vm} : ObservableObject
{{
    public void OnShown() {{ }}
    public void OnHidden() {{ }}
}}
""")

# ───────── App
csproj("NotchHub.App", wpf=True, output="WinExe", refs=[
    "NotchHub.Core", "NotchHub.UI", "NotchHub.KdeConnect", "NotchHub.Platform.Media", "NotchHub.Platform.Connectivity",
    "NotchHub.Platform.Machine", "NotchHub.Platform.Shell", "NotchHub.Services", "NotchHub.Demo",
    "NotchHub.Features.Controls", "NotchHub.Features.Today", "NotchHub.Features.Stats", "NotchHub.Features.Clipboard",
    "NotchHub.Features.Phone", "NotchHub.Features.Power", "NotchHub.Features.Settings", "NotchHub.Features.Compact"],
    extra="""
  <PropertyGroup>
    <AssemblyName>NotchHub</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
""")
print("ok")
