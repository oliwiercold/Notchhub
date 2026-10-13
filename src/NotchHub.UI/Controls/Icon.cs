using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NotchHub.UI.Controls;

/// <summary>
/// Material Symbols Rounded icon. Usage: &lt;ui:Icon Glyph="wifi" Size="20" Filled="True" /&gt;
/// Foreground is inherited like text (TextElement.Foreground). The glyph's em square is centered in the element.
/// </summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(Icon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface OutlineFace = new(NhFonts.Symbols, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface FilledFace = new(NhFonts.SymbolsFilled, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Material Symbols name, e.g. "wifi". Unknown names render a question mark.</summary>
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    /// <summary>Icon size in DIPs (the em square), default 20.</summary>
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Glyph) || Size <= 0) return;
        var text = IconMap.Get(Glyph);
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Filled ? FilledFace : OutlineFace, Size, Foreground ?? Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        // Material Symbols glyphs live in the em square [0, 1em] above the baseline.
        var baselineY = (ActualHeight + Size) / 2.0;
        var x = (ActualWidth - Size) / 2.0;
        dc.DrawText(ft, new Point(x, baselineY - ft.Baseline));
    }
}
