using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotchHub.Core;
using NotchHub.UI.Localization;

namespace NotchHub.UI.Converters;

/// <summary>
/// Base of all converters: also a markup extension, so they can be used inline without resources:
/// <c>Visibility="{Binding IsX, Converter={conv:BoolToVisibility}, ConverterParameter=Invert}"</c>.
/// </summary>
public abstract class ConverterBase : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);
    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    protected static bool IsInvert(object? parameter) =>
        parameter is string s && (s.Equals("Invert", StringComparison.OrdinalIgnoreCase) || s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        || parameter is true;

    protected static double ToDouble(object? v, double fallback = 0) => v switch
    {
        null => fallback,
        double d => d,
        IConvertible c => TryConvert(c, fallback),
        _ => fallback,
    };

    private static double TryConvert(IConvertible c, double fallback)
    {
        try { return c.ToDouble(CultureInfo.InvariantCulture); }
        catch (Exception) { return fallback; }
    }
}

/// <summary>true → Visible, false → Collapsed. ConverterParameter=Invert flips it.</summary>
public sealed class BoolToVisibility : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ IsInvert(parameter) ? Visibility.Visible : Visibility.Collapsed;

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ IsInvert(parameter);
}

/// <summary>null (or empty string) → Collapsed, otherwise Visible. ConverterParameter=Invert flips it.</summary>
public sealed class NullToVisibility : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is not null && value is not "") ^ IsInvert(parameter) ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class InverseBool : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>byte[] (PNG/JPEG…) → frozen BitmapImage decoded at ConverterParameter px width (default 96). Cached per array.</summary>
public sealed class BytesToImage : ConverterBase
{
    private static readonly ConditionalWeakTable<byte[], Dictionary<int, ImageSource>> Cache = new();

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes) return null;
        var width = (int)ToDouble(parameter, 96);
        var perArray = Cache.GetOrCreateValue(bytes);
        lock (perArray)
        {
            if (perArray.TryGetValue(width, out var cached)) return cached;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (width > 0) bmp.DecodePixelWidth = width;
                bmp.StreamSource = new MemoryStream(bytes, writable: false);
                bmp.EndInit();
                bmp.Freeze();
                perArray[width] = bmp;
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Warn("BytesToImage: decode failed", ex);
                return null;
            }
        }
    }
}

/// <summary>File path → frozen BitmapImage decoded at ConverterParameter px width (default 96). Small LRU-ish cache.</summary>
public sealed class PathToImage : ConverterBase
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } path) return null;
        var width = (int)ToDouble(parameter, 96);
        var key = width + "|" + path;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (width > 0) bmp.DecodePixelWidth = width;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            lock (Cache)
            {
                if (Cache.Count > 96) Cache.Clear();
                Cache[key] = bmp;
            }
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warn("PathToImage: decode failed", ex);
            return null;
        }
    }
}

/// <summary>string.Format with the UI culture. ConverterParameter = "{0:N1} GB" or just "N1".</summary>
public sealed class Format : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string fmt || fmt.Length == 0) return value?.ToString() ?? "";
        try
        {
            return fmt.Contains('{') ? string.Format(Loc.Culture, fmt, value) : string.Format(Loc.Culture, "{0:" + fmt + "}", value);
        }
        catch (FormatException) { return value?.ToString() ?? ""; }
    }
}

/// <summary>TimeSpan (or seconds as number) → "m:ss" / "h:mm:ss".</summary>
public sealed class TimeSpanClock : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ts = value switch
        {
            TimeSpan t => t,
            null => TimeSpan.Zero,
            _ => TimeSpan.FromSeconds(Math.Max(0, ToDouble(value))),
        };
        return ToClock(ts);
    }

    public static string ToClock(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes}:{ts.Seconds:00}";
    }
}

/// <summary>DateTime / DateTimeOffset → localized "now", "5m ago", "3h ago", "2d ago" (Loc common.*).</summary>
public sealed class RelativeTime : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        DateTimeOffset? when = value switch
        {
            DateTimeOffset d => d,
            DateTime d => d.Kind == DateTimeKind.Unspecified ? new DateTimeOffset(d, TimeZoneInfo.Local.GetUtcOffset(d)) : new DateTimeOffset(d),
            _ => null,
        };
        return when is null ? "" : Describe(when.Value);
    }

    public static string Describe(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        if (span.TotalMinutes < 1) return Loc.Get("common.now");
        if (span.TotalHours < 1) return Loc.Format("common.minutesAgo", (int)span.TotalMinutes);
        if (span.TotalDays < 1) return Loc.Format("common.hoursAgo", (int)span.TotalHours);
        return Loc.Format("common.daysAgo", (int)span.TotalDays);
    }
}

/// <summary>value.ToString() == ConverterParameter → true. ConvertBack (true) parses the parameter into the enum (RadioButton-style).</summary>
public sealed class EnumEquals : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var eq = value is not null && parameter is not null &&
                 string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        return targetType == typeof(Visibility) ? (eq ? Visibility.Visible : Visibility.Collapsed) : eq;
    }

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return value is true && parameter is not null && type.IsEnum && Enum.TryParse(type, parameter.ToString(), true, out var result)
            ? result
            : Binding.DoNothing;
    }
}

/// <summary>0 / null / empty string / empty collection → Collapsed, else Visible. ConverterParameter=Invert flips it.</summary>
public sealed class ZeroToCollapsed : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var zero = value switch
        {
            null => true,
            string s => s.Length == 0,
            ICollection c => c.Count == 0,
            IConvertible => ToDouble(value, 0) == 0,
            _ => false,
        };
        return zero ^ IsInvert(parameter) ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>number × ConverterParameter (invariant double, default 1).</summary>
public sealed class Multiply : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var r = ToDouble(value) * ToDouble(parameter, 1);
        if (targetType == typeof(GridLength)) return new GridLength(Math.Max(0, r));
        if (targetType == typeof(Thickness)) return new Thickness(r);
        if (targetType == typeof(CornerRadius)) return new CornerRadius(Math.Max(0, r));
        return r;
    }
}
