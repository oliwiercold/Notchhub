using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotchHub.Core.Settings;
using NotchHub.UI.Animation;
using NotchHub.UI.Localization;
using NotchHub.UI.Theming;

namespace NotchHub.Gallery;

/// <summary>Renders the gallery twice (default tokens | generated scheme) side by side into one PNG, then exits.</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "gallery.png");
        var hex = args.Length > 1 ? args[1] : "#3F6FD8";
        var variant = args.Length > 2 && Enum.TryParse<SchemeVariant>(args[2], true, out var v) ? v : SchemeVariant.TonalSpot;
        var source = (Color)ColorConverter.ConvertFromString(hex);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/NotchHub.UI;component/Themes/All.xaml") });
        Loc.SetLanguage("en");
        Motion.ReduceMotion = true;

        var scheme = ThemeService.Generate(source, variant, 0, PanelStyle.Black);
        foreach (var k in new[] { "Primary", "OnPrimary", "Surface1", "Surface2", "SecondaryContainer", "Tertiary", "Success" })
            Console.WriteLine($"{k,-20} {scheme[k]}");
        var gold = ThemeService.Generate(Color.FromRgb(0xD4, 0xAF, 0x5A), SchemeVariant.TonalSpot, 0, PanelStyle.Black);
        Console.WriteLine($"gold Primary {gold["Primary"]}  Surface1 {gold["Surface1"]}");

        var panel = new GalleryPanel();
        var window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight, Content = panel,
        };
        window.Show();

        BitmapSource? first = null;
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            if (step == 0)
            {
                first = Render(panel);
                panel.Caption.Text = $"GENERATED · {hex} · {variant}".ToUpper(CultureInfo.InvariantCulture);
                ThemeService.Apply(scheme, animate: false);
                step = 1;
                return;
            }
            timer.Stop();
            var second = Render(panel);
            Save(first!, second, output);
            Console.WriteLine("saved " + output);
            app.Shutdown();
        };
        timer.Start();
        app.Run();
        return 0;
    }

    private static RenderTargetBitmap Render(FrameworkElement el)
    {
        el.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth), (int)Math.Ceiling(el.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(el);
        bmp.Freeze();
        return bmp;
    }

    private static void Save(BitmapSource a, BitmapSource b, string path)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x60, 0x66, 0x70)), null, new Rect(0, 0, a.Width + b.Width + 30, Math.Max(a.Height, b.Height) + 20));
            dc.DrawImage(a, new Rect(10, 10, a.Width, a.Height));
            dc.DrawImage(b, new Rect(a.Width + 20, 10, b.Width, b.Height));
        }
        var rtb = new RenderTargetBitmap((int)(a.Width + b.Width + 30), (int)(Math.Max(a.Height, b.Height) + 20), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
