using System.Windows.Media;

namespace NotchHub.UI;

/// <summary>
/// Bundled fonts (static instances of Google Sans Flex and Material Symbols Rounded, see tools/fonts/instance_fonts.py).
/// WPF ignores variable-font axes, so every weight is a separate file.
///  * <see cref="Sans"/>: weights 400 Regular, 500 Medium, 600 SemiBold, 700 Bold, 800 ExtraBold.
///  * <see cref="SansRounded"/>: rounded terminals (ROND=100), weights 500, 700, 800 — for big numerals (clock, gauges).
///  * <see cref="Symbols"/> / <see cref="SymbolsFilled"/>: Material Symbols Rounded, outline / filled (use the <see cref="Controls.Icon"/> control).
/// XAML resource keys: Font.Sans, Font.SansRounded, Font.Symbols, Font.SymbolsFilled.
/// </summary>
public static class NhFonts
{
    private static readonly Uri BaseUri = new("pack://application:,,,/NotchHub.UI;component/");

    public static readonly FontFamily Sans = new(BaseUri, "./Assets/Fonts/#NH Sans, Segoe UI Variable Text, Segoe UI, Segoe UI Emoji");
    public static readonly FontFamily SansRounded = new(BaseUri, "./Assets/Fonts/#NH Sans Rounded, Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Symbols = new(BaseUri, "./Assets/Fonts/#NH Symbols");
    public static readonly FontFamily SymbolsFilled = new(BaseUri, "./Assets/Fonts/#NH Symbols Filled");
}
