using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NotchHub.Features.Settings;

public enum SettingsSection { Appearance, Notch, Shortcuts, Clipboard, Weather, Calendar, MeetingBar, Phone, Mods, General }

/// <summary>One option of a single-choice chip group.</summary>
public sealed partial class Choice(string label, object value, Action<Choice> pick, string icon = "") : ObservableObject
{
    public string Label { get; } = label;
    public object Value { get; } = value;
    public string Icon { get; } = icon;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Re-raises IsSelected even when unchanged (a ToggleButton may have flipped its own visual state).</summary>
    internal void Sync(bool selected)
    {
        IsSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }

    [RelayCommand] private void Pick() => pick(this);
}

public static class Choices
{
    public static ObservableCollection<Choice> Build(IEnumerable<(string Label, object Value)> options, object current, Action<object> onPick,
        Func<object, string>? icon = null)
    {
        var list = new ObservableCollection<Choice>();
        void Picked(Choice c)
        {
            foreach (var x in list) x.Sync(ReferenceEquals(x, c));
            onPick(c.Value);
        }
        foreach (var (label, value) in options)
            list.Add(new Choice(label, value, Picked, icon?.Invoke(value) ?? "") { IsSelected = Equals(value, current) });
        return list;
    }

    public static void Select(IEnumerable<Choice> list, object value)
    {
        foreach (var c in list) c.Sync(Equals(c.Value, value));
    }
}

/// <summary>Android-style color swatch (circle: top half A, bottom-left B, bottom-right C).</summary>
public sealed partial class Swatch(string label, object key, Color source, Brush a, Brush b, Brush c, Action<Swatch> pick) : ObservableObject
{
    public string Label { get; } = label;
    public object Key { get; } = key;
    public Color Source { get; } = source;
    public Brush A { get; } = a;
    public Brush B { get; } = b;
    public Brush C { get; } = c;

    [ObservableProperty] private bool _isSelected;

    internal void Sync(bool selected)
    {
        IsSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }

    [RelayCommand] private void Pick() => pick(this);
}

public sealed partial class ColorDot(string hex, Action<ColorDot> pick) : ObservableObject
{
    public string Hex { get; } = hex;
    public Brush Brush { get; } = ColorUtil.BrushOf(ColorUtil.Parse(hex) ?? Colors.Gray);
    [ObservableProperty] private bool _isSelected;
    [RelayCommand] private void Pick() => pick(this);
}

public sealed partial class TextItem(string text, Action<TextItem> remove) : ObservableObject
{
    public string Text { get; } = text;
    [RelayCommand] private void Remove() => remove(this);
}

public sealed partial class HotkeyField(string id, string titleKey, string gesture, Action<HotkeyField> start) : ObservableObject
{
    public string Id { get; } = id;
    public string TitleKey { get; } = titleKey;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Display))] private string _gesture = gesture;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Display))] private bool _isCapturing;
    [ObservableProperty] private string? _error;

    public string Title => NotchHub.UI.Localization.Loc.Get(TitleKey);
    public string Display => NotchHub.UI.Localization.Loc.Get(IsCapturing ? "settings.Shortcuts.Press" : "settings.Shortcuts.None") is var t
                             && (IsCapturing || string.IsNullOrEmpty(Gesture)) ? t : Gesture;

    [RelayCommand] private void Start() => start(this);
}

public sealed partial class LocationResult(Core.Contracts.GeoLocation loc, Action<LocationResult> pick) : ObservableObject
{
    public Core.Contracts.GeoLocation Location { get; } = loc;
    public string Name => Location.Name;
    public string Detail => string.Join(", ", new[] { Location.Region, Location.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
    [RelayCommand] private void Pick() => pick(this);
}

public sealed partial class SubscriptionItem(Core.Settings.CalendarSubscription sub, Action<SubscriptionItem> changed, Action<SubscriptionItem> remove)
    : ObservableObject
{
    public string Id { get; } = sub.Id;
    public string Name { get; } = string.IsNullOrWhiteSpace(sub.Name) ? sub.Url : sub.Name;
    public string Url { get; } = sub.Url;
    public Brush Brush { get; } = ColorUtil.BrushOf(ColorUtil.Parse(sub.ColorHex) ?? Colors.HotPink);
    [ObservableProperty] private bool _enabled = sub.Enabled;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _hasError;

    partial void OnEnabledChanged(bool value) => changed(this);
    [RelayCommand] private void Remove() => remove(this);
}

public sealed partial class DeviceItem(Core.Contracts.PhoneDevice d, string status, Action<DeviceItem> unpair) : ObservableObject
{
    public string Id { get; } = d.Id;
    public string Name { get; } = d.Name;
    public string Icon { get; } = d.DeviceType == "tablet" ? "tablet" : d.DeviceType is "desktop" or "laptop" ? "computer" : "smartphone";
    public string Status { get; } = status;
    [ObservableProperty] private bool _isBusy;
    [RelayCommand] private void Unpair() => unpair(this);
}

internal static class ColorUtil
{
    public static Color? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(ch => new string(ch, 2)));
        if (h.Length != 6 || !uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Brush BrushOf(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static Color WindowsAccent()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
                return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
        }
        catch { /* fall through */ }
        var g = SystemParameters.WindowGlassColor;
        return Color.FromRgb(g.R, g.G, g.B);
    }
}

/// <summary>Monitor enumeration (same order as EnumDisplayMonitors).</summary>
internal static class Monitors
{
    public sealed record Info(int Width, int Height, bool IsPrimary);

    public static List<Info> List()
    {
        var result = new List<Info>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(h, ref mi))
                    result.Add(new Info(mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top, (mi.dwFlags & 1) != 0));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { Core.Log.Warn("Settings: monitor enumeration failed", ex); }
        return result;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
}

/// <summary>Settings row: icon, title, description, control on the right (Content).</summary>
public class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(object), typeof(SettingRow));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(object), typeof(SettingRow));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public object? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}
