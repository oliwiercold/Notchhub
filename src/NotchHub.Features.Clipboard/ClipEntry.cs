using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Clipboard;

/// <summary>Row / preview model wrapping an immutable <see cref="ClipItem"/>.</summary>
public sealed partial class ClipEntry : ObservableObject
{
    private const int ThumbDecode = 64;
    private const int PreviewDecode = 720;
    private bool _thumbRequested, _previewRequested;
    private ImageSource? _thumb, _previewImage;
    private const int SearchPrefix = 32_000;
    private volatile string? _searchKey;

    public ClipEntry(ClipItem item, string group)
    {
        Item = item;
        Group = group;
        Title = BuildTitle(item);
        if (item.Kind == ClipKind.Color && TryParseColor(item.Text, out var c))
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            Swatch = b;
            Hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}" + (c.A < 255 ? $"{c.A:X2}" : "");
            Rgb = c.A < 255
                ? string.Create(CultureInfo.InvariantCulture, $"rgba({c.R}, {c.G}, {c.B}, {c.A / 255.0:0.##})")
                : $"rgb({c.R}, {c.G}, {c.B})";
        }
    }

    public ClipItem Item { get; }
    public Guid Id => Item.Id;
    public ClipKind Kind => Item.Kind;
    public bool IsPinned => Item.IsPinned;
    public bool IsPhone => Item.Origin == ClipOrigin.Phone;
    public DateTimeOffset CopiedAt => Item.CopiedAt;
    public string Group { get; }
    public string Title { get; }
    public string Glyph => Item.Kind switch
    {
        ClipKind.Link => "link",
        ClipKind.Code => "code",
        ClipKind.Image => "image",
        ClipKind.Color => "palette",
        ClipKind.Files => "description",
        _ => "subject",
    };

    public bool HasThumb => Item.Kind == ClipKind.Image;
    public Brush? Swatch { get; }
    public string? Hex { get; }
    public string? Rgb { get; }
    public string FullText => Item.Text ?? "";
    public bool IsCode => Item.Kind == ClipKind.Code;
    public bool IsTextual => Item.Kind is ClipKind.Text or ClipKind.Code;

    public IReadOnlyList<string> FileList => Item.Files ?? (Item.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public IEnumerable<string> FileNames => FileList.Select(SafeFileName);

    public string LinkDomain
    {
        get
        {
            if (Uri.TryCreate((Item.Text ?? "").Trim(), UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host))
                return u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
            return (Item.Text ?? "").Trim();
        }
    }

    /// <summary>Small thumbnail, decoded lazily off the UI thread (only rows that are realized ask for it).</summary>
    public ImageSource? Thumb
    {
        get
        {
            if (!_thumbRequested && HasThumb)
            {
                _thumbRequested = true;
                var path = Item.ThumbnailPath ?? Item.ImagePath;
                if (path is not null) Load(path, ThumbDecode, img => { _thumb = img; OnPropertyChanged(nameof(Thumb)); }, ref _thumb);
            }
            return _thumb;
        }
    }

    /// <summary>Preview-size image (fit into the preview card).</summary>
    public ImageSource? PreviewImage
    {
        get
        {
            if (!_previewRequested && HasThumb)
            {
                _previewRequested = true;
                var path = Item.ImagePath ?? Item.ThumbnailPath;
                var w = Item.ImageWidth > 0 ? Math.Min(Item.ImageWidth, PreviewDecode) : PreviewDecode;
                if (path is not null) Load(path, w, img => { if (!_previewRequested) return; _previewImage = img; OnPropertyChanged(nameof(PreviewImage)); }, ref _previewImage);
            }
            return _previewImage ?? Thumb;
        }
    }

    /// <summary>Forgets the large preview decode (the entry is no longer the current one, or the tab was hidden).</summary>
    public void ReleasePreview()
    {
        _previewRequested = false;
        _previewImage = null;
    }

    private static void Load(string path, int width, Action<ImageSource?> done, ref ImageSource? field)
    {
        if (ImageLoader.TryGetCached(path, width, out var cached)) { field = cached; return; }
        ImageLoader.LoadAsync(path, width).ContinueWith(t =>
        {
            var img = t.IsCompletedSuccessfully ? t.Result : null;
            if (img is not null) AppServices.Ui.Post(() => done(img));
        }, TaskScheduler.Default);
    }

    // ─────────── localized metadata ───────────

    public string TypeText => Loc.Get("clipboard.kind." + Item.Kind.ToString().ToLowerInvariant());

    public string SizeText
    {
        get
        {
            switch (Item.Kind)
            {
                case ClipKind.Image:
                    return Item.ImageWidth > 0 ? $"{Item.ImageWidth}×{Item.ImageHeight}" : "—";
                case ClipKind.Files:
                    return Plural("clipboard.files", FileList.Count);
                case ClipKind.Color:
                    return Rgb ?? FullText;
                default:
                    var t = FullText;
                    var words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                    var lines = t.Length == 0 ? 0 : t.TrimEnd('\r', '\n').Split('\n').Length;
                    return $"{Plural("clipboard.chars", t.Length)} • {Plural("clipboard.words", words)} • {Plural("clipboard.lines", lines)}";
            }
        }
    }

    public string CopiedText
    {
        get
        {
            var local = Item.CopiedAt.ToLocalTime();
            var time = local.ToString("t", Loc.Culture);
            var days = (DateTime.Today - local.Date).Days;
            if (days == 0) return Loc.Format("clipboard.todayAt", time);
            if (days == 1) return Loc.Format("clipboard.yesterdayAt", time);
            return local.ToString("d MMM yyyy", Loc.Culture) + ", " + time;
        }
    }

    public string SourceText => IsPhone ? Loc.Get("clipboard.phone") : (string.IsNullOrWhiteSpace(Item.SourceApp) ? "—" : Item.SourceApp!);

    public bool HasSearchKey => _searchKey is not null;

    /// <summary>
    /// Builds the normalized search key from a bounded prefix of the text (items can be ~1M chars). Pure and thread-safe, so the
    /// view model can warm the keys off the UI thread.
    /// </summary>
    public void EnsureSearchKey()
    {
        if (_searchKey is not null) return;
        var text = Item.Text ?? "";
        if (text.Length > SearchPrefix) text = text[..SearchPrefix];
        _searchKey = Normalize(string.Join("\n", text, Item.SourceApp ?? "", Item.Files is { } f ? string.Join("\n", f) : ""));
    }

    /// <summary>Frees the normalized copy of the text (tab hidden).</summary>
    public void ReleaseSearchKey() => _searchKey = null;

    public bool Matches(string normalizedQuery)
    {
        if (normalizedQuery.Length == 0) return true;
        EnsureSearchKey();
        if (_searchKey is { } key && key.Contains(normalizedQuery, StringComparison.Ordinal)) return true;
        // localized kind label (read on the UI thread only), so "obraz" / "image" finds image clips
        return Item.Kind == ClipKind.Image && Normalize(TypeText).Contains(normalizedQuery, StringComparison.Ordinal);
    }

    // ─────────── helpers ───────────

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch == 'ł' ? 'l' : ch);
        }
        return sb.ToString();
    }

    public static string Plural(string key, int n)
    {
        var form = n == 1 ? "one" : (n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) ? "few" : "many";
        return Loc.Format(key + "." + form, n);
    }

    private static string SafeFileName(string p)
    {
        try { var n = Path.GetFileName(p.TrimEnd('\\', '/')); return string.IsNullOrEmpty(n) ? p : n; }
        catch { return p; }
    }

    private string BuildTitle(ClipItem item)
    {
        switch (item.Kind)
        {
            case ClipKind.Image:
                return item.ImageWidth > 0 ? $"{Loc.Get("clipboard.kind.image")} • {item.ImageWidth}×{item.ImageHeight}" : Loc.Get("clipboard.kind.image");
            case ClipKind.Files:
                var files = FileList;
                if (files.Count == 0) return Loc.Get("clipboard.kind.files");
                var first = SafeFileName(files[0]);
                return files.Count == 1 ? first : $"{first} • {Plural("clipboard.files", files.Count)}";
            default:
                var t = item.Text ?? "";
                if (t.Length > 240) t = t[..240];
                var sb = new StringBuilder(t.Length);
                var space = false;
                foreach (var ch in t)
                {
                    if (char.IsWhiteSpace(ch)) { if (!space && sb.Length > 0) sb.Append(' '); space = true; }
                    else { sb.Append(ch); space = false; }
                }
                return sb.ToString().TrimEnd();
        }
    }

    internal static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return false;
        try
        {
            if (t.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                var open = t.IndexOf('('); var close = t.IndexOf(')');
                if (open < 0 || close < open) return false;
                var parts = t[(open + 1)..close].Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) return false;
                byte P(string s) => (byte)Math.Clamp(double.Parse(s.TrimEnd('%'), CultureInfo.InvariantCulture), 0, 255);
                var a = parts.Length > 3 ? (byte)Math.Clamp(double.Parse(parts[3], CultureInfo.InvariantCulture) * (parts[3].Contains('.') || parts[3] == "1" || parts[3] == "0" ? 255 : 1), 0, 255) : (byte)255;
                color = Color.FromArgb(a, P(parts[0]), P(parts[1]), P(parts[2]));
                return true;
            }
            if (!t.StartsWith('#')) t = "#" + t;
            var hex = t[1..];
            if (hex.Length == 8) hex = hex[6..] + hex[..6]; // CSS #RRGGBBAA → WPF #AARRGGBB
            if (ColorConverter.ConvertFromString("#" + hex) is Color c) { color = c; return true; }
        }
        catch (FormatException) { }
        catch (OverflowException) { }
        return false;
    }
}
