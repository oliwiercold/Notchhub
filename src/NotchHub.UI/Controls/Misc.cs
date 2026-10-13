using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NotchHub.UI.Animation;
using NotchHub.UI.Theming.Mcu;

namespace NotchHub.UI.Controls;

/// <summary>Attached properties for the TextField style: ui:Field.Placeholder, ui:Field.Icon.</summary>
public static class Field
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Field), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Field), new FrameworkPropertyMetadata(""));

    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);
    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string value) => d.SetValue(IconProperty, value);
}

/// <summary>Attached icon for the FilterChip style: ui:Chip.Icon (hidden while the chip shows its check mark).</summary>
public static class Chip
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Chip), new FrameworkPropertyMetadata(""));

    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string value) => d.SetValue(IconProperty, value);
}

/// <summary>Count badge (Error / OnError pill, min 16×16). Empty content → small 8 px dot.</summary>
public class Badge : ContentControl;

/// <summary>Circular avatar: image, or initials on a color derived from the text.</summary>
public class Avatar : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Avatar), new FrameworkPropertyMetadata("", (d, _) => ((Avatar)d).Update()));
    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(ImageSource), typeof(Avatar), new FrameworkPropertyMetadata(null, (d, _) => ((Avatar)d).Update()));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Avatar), new FrameworkPropertyMetadata(28.0, (d, _) => ((Avatar)d).Update()));

    private static readonly DependencyPropertyKey InitialsKey = DependencyProperty.RegisterReadOnly(
        nameof(Initials), typeof(string), typeof(Avatar), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty InitialsProperty = InitialsKey.DependencyProperty;
    private static readonly DependencyPropertyKey FillKey = DependencyProperty.RegisterReadOnly(
        nameof(Fill), typeof(Brush), typeof(Avatar), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty FillProperty = FillKey.DependencyProperty;
    private static readonly DependencyPropertyKey TextBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(TextBrush), typeof(Brush), typeof(Avatar), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty TextBrushProperty = TextBrushKey.DependencyProperty;

    private static readonly Dictionary<int, (Brush Bg, Brush Fg)> HueCache = [];

    public Avatar() => Update();

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ImageSource? Image { get => (ImageSource?)GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public string Initials => (string)GetValue(InitialsProperty);
    /// <summary>Circle fill: ImageBrush when <see cref="Image"/> is set, else the text-derived color.</summary>
    public Brush? Fill => (Brush?)GetValue(FillProperty);
    public Brush? TextBrush => (Brush?)GetValue(TextBrushProperty);

    private void Update()
    {
        var text = (Text ?? "").Trim();
        var words = text.Split([' ', '.', '_', '-', '@'], StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length switch
        {
            0 => "",
            1 => char.ToUpper(words[0][0]).ToString(),
            _ => string.Concat(char.ToUpper(words[0][0]), char.ToUpper(words[1][0])),
        };
        if (Size < 24 && initials.Length > 1) initials = initials[..1];
        if (Image is not null) initials = "";
        SetValue(InitialsKey, initials);
        FontSize = Math.Max(8, Size * (initials.Length > 1 ? 0.36 : 0.44));

        var hash = 17;
        foreach (var ch in text) hash = unchecked(hash * 31 + ch);
        var hue = (int)((uint)hash % 24) * 15;
        (Brush Bg, Brush Fg) pair;
        lock (HueCache)
        {
            if (!HueCache.TryGetValue(hue, out pair))
            {
                pair = (Frozen(Hct.Solve(hue, 36, 32)), Frozen(Hct.Solve(hue, 30, 90)));
                HueCache[hue] = pair;
            }
        }
        SetValue(TextBrushKey, pair.Fg);
        if (Image is { } img)
        {
            var ib = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            ib.Freeze();
            SetValue(FillKey, ib);
        }
        else SetValue(FillKey, pair.Bg);
    }

    private static SolidColorBrush Frozen(int argb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }
}

/// <summary>
/// In-panel bottom sheet host. <c>Content</c> = the page; <see cref="SheetContent"/> slides up over a scrim when
/// <see cref="IsOpen"/>. Esc or a click on the scrim closes it (IsOpen binds two-way).
/// </summary>
public class SheetHost : ContentControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(SheetHost),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SheetHost)d).OnIsOpenChanged()));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SheetHost), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty SheetContentProperty = DependencyProperty.Register(
        nameof(SheetContent), typeof(object), typeof(SheetHost), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty SheetContentTemplateProperty = DependencyProperty.Register(
        nameof(SheetContentTemplate), typeof(DataTemplate), typeof(SheetHost), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty SheetWidthProperty = DependencyProperty.Register(
        nameof(SheetWidth), typeof(double), typeof(SheetHost), new FrameworkPropertyMetadata(double.NaN));

    private FrameworkElement? _scrim, _sheet;
    private readonly TranslateTransform _slide = new();

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? SheetContent { get => GetValue(SheetContentProperty); set => SetValue(SheetContentProperty, value); }
    public DataTemplate? SheetContentTemplate { get => (DataTemplate?)GetValue(SheetContentTemplateProperty); set => SetValue(SheetContentTemplateProperty, value); }
    /// <summary>Fixed sheet width (NaN = stretch).</summary>
    public double SheetWidth { get => (double)GetValue(SheetWidthProperty); set => SetValue(SheetWidthProperty, value); }

    /// <summary>Raised after the sheet was closed by the user (Esc / scrim).</summary>
    public event EventHandler? Dismissed;

    public override void OnApplyTemplate()
    {
        if (_scrim is not null) _scrim.MouseLeftButtonDown -= OnScrimDown;
        base.OnApplyTemplate();
        _scrim = GetTemplateChild("PART_Scrim") as FrameworkElement;
        _sheet = GetTemplateChild("PART_Sheet") as FrameworkElement;
        if (_scrim is not null) _scrim.MouseLeftButtonDown += OnScrimDown;
        if (_sheet is not null) _sheet.RenderTransform = _slide;
        ApplyState(false);
    }

    private void OnScrimDown(object sender, MouseButtonEventArgs e)
    {
        Dismiss();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (IsOpen && e.Key == Key.Escape)
        {
            Dismiss();
            e.Handled = true;
        }
    }

    private void Dismiss()
    {
        SetCurrentValue(IsOpenProperty, false);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void OnIsOpenChanged() => ApplyState(IsLoaded && !Motion.ReduceMotion);

    private void ApplyState(bool animate)
    {
        if (_scrim is null || _sheet is null) return;
        var open = IsOpen;
        var offscreen = Math.Max(ActualHeight, 400);
        if (open)
        {
            _scrim.Visibility = Visibility.Visible;
            _sheet.Visibility = Visibility.Visible;
            if (animate)
            {
                if (_slide.Y == 0 && !_slide.HasAnimatedProperties) _slide.Y = offscreen;
                _slide.Animate(TranslateTransform.YProperty, 0, Springs.SpatialDefault);
                _scrim.Animate(OpacityProperty, 1, Springs.EffectsDefault);
            }
            else
            {
                _slide.BeginAnimation(TranslateTransform.YProperty, null);
                _slide.Y = 0;
                _scrim.BeginAnimation(OpacityProperty, null);
                _scrim.Opacity = 1;
            }
            Dispatcher.BeginInvoke(() =>
            {
                if (IsOpen && _sheet is not null && !_sheet.IsKeyboardFocusWithin) _sheet.Focus();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            if (animate)
            {
                _slide.Animate(TranslateTransform.YProperty, offscreen, Springs.EffectsDefault, () =>
                {
                    if (IsOpen || _sheet is null) return;
                    _sheet.Visibility = Visibility.Collapsed;
                    _slide.BeginAnimation(TranslateTransform.YProperty, null);
                    _slide.Y = 0;
                });
                _scrim.Animate(OpacityProperty, 0, Springs.EffectsDefault, () =>
                {
                    if (!IsOpen && _scrim is not null) _scrim.Visibility = Visibility.Collapsed;
                });
            }
            else
            {
                _slide.BeginAnimation(TranslateTransform.YProperty, null);
                _slide.Y = 0;
                _scrim.BeginAnimation(OpacityProperty, null);
                _scrim.Opacity = 0;
                _scrim.Visibility = Visibility.Collapsed;
                _sheet.Visibility = Visibility.Collapsed;
            }
        }
    }
}
