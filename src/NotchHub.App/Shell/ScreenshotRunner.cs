using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.App.Shell;

/// <summary>--screenshot &lt;dir&gt;: renders every notch state over a wallpaper backdrop into PNGs.</summary>
public static class ScreenshotRunner
{
    private const double Scale = 2;

    public static async Task RunAsync(NotchWindow window, string dir)
    {
        Directory.CreateDirectory(dir);
        var backdrop = await CreateBackdropAsync();

        async Task Shot(string name)
        {
            await window.SettleAsync(900);
            Save(window, backdrop, Path.Combine(dir, name + ".png"));
            Log.Info($"Screenshot {name}");
        }

        window.SuppressLiveActivity = true;
        window.SetState(ShellState.Collapsed, force: true);
        await Shot("collapsed");

        window.SuppressLiveActivity = false;
        window.SetState(ShellState.Collapsed, force: true);
        await Shot("live");

        window.SetState(ShellState.Peek);
        await Shot("peek");

        window.ShowHud(new HudRequest(HudKind.Volume, new HudLevel(0.62), TimeSpan.FromMinutes(1)));
        await Shot("hud-volume");

        var note = AppServices.Phone?.Notifications.FirstOrDefault() ?? new PhoneNotification(
            "demo", "Messages", "Ania", "Are we still on for 7?", null, DateTimeOffset.Now, true, false, null, [], null);
        window.ShowHud(new HudRequest(HudKind.PhoneNotification, note, TimeSpan.FromMinutes(1)));
        await Shot("hud-notification");

        window.SetState(ShellState.Collapsed);
        foreach (var tab in NotchWindow.StripTabs.Append(ShellTab.Settings))
        {
            window.Expand(tab, activate: false);
            await Shot($"expanded-{tab.ToString().ToLowerInvariant()}");
        }
    }

    private static void Save(NotchWindow window, Brush backdrop, string path)
    {
        var size = new Size(NotchWindow.WindowWidth, NotchWindow.WindowHeight);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(backdrop, null, new Rect(size));
        var rtb = new RenderTargetBitmap((int)(size.Width * Scale), (int)(size.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        rtb.Render(bg);
        rtb.Render(window.Root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static async Task<Brush> CreateBackdropAsync()
    {
        try
        {
            var wp = AppServices.Wallpaper;
            if (wp?.CurrentPath is { } p && File.Exists(p))
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(p);
                img.DecodePixelWidth = 2400;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return Brush(img);
            }
            if (wp is not null && await wp.GetPixelsAsync(512) is { } px)
            {
                var src = BitmapSource.Create(px.Width, px.Height, 96, 96, PixelFormats.Bgra32, null, px.Argb, px.Width * 4);
                src.Freeze();
                return Brush(src);
            }
        }
        catch (Exception ex) { Log.Warn("Screenshot backdrop failed", ex); }
        return new LinearGradientBrush(Color.FromRgb(0x3A, 0x4A, 0x6B), Color.FromRgb(0xC9, 0x8F, 0x6A), 45);

        static Brush Brush(BitmapSource s) => new ImageBrush(s) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top };
    }
}
