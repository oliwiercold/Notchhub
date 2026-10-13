using System.Windows;
using System.Windows.Media;
using NotchHub.Core.Settings;
using NotchHub.UI.Theming;

// Regression check: theme can be re-applied many times (brushes must never end up frozen).
int exit = 0;
var t = new Thread(() => { exit = Run(); });
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
return exit;

static int Run()
{
var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/NotchHub.UI;component/Themes/All.xaml") });
var colors = new[] { Color.FromRgb(0xD4, 0xAF, 0x5A), Color.FromRgb(0x4A, 0x7B, 0xD8), Color.FromRgb(0x5B, 0xA3, 0x5B), Color.FromRgb(0xC0, 0x4A, 0x8A) };
int failures = 0;
// animations only tick while WPF renders something: show a tiny off-screen window bound to the brush
var probe = new Window { Width = 40, Height = 40, Left = -2000, Top = -2000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
probe.SetResourceReference(Window.BackgroundProperty, "Brush.Primary");
probe.Show();
app.Dispatcher.InvokeAsync(async () =>
{
    for (int round = 0; round < 6; round++)
    {
        var map = ThemeService.Generate(colors[round % colors.Length], SchemeVariant.TonalSpot, 0, PanelStyle.Black);
        try { ThemeService.Apply(map, animate: round % 2 == 0); }
        catch (Exception ex) { failures++; Console.WriteLine($"round {round}: {ex.GetType().Name}: {ex.Message}"); }
        await Task.Delay(450);
        var b = (SolidColorBrush)app.Resources["Brush.Primary"];
        Console.WriteLine($"round {round}: Brush.Primary={b.Color} expected={map["Primary"]} frozen={b.IsFrozen}");
        if ((b.Color.A, b.Color.R, b.Color.G, b.Color.B) != (map["Primary"].A, map["Primary"].R, map["Primary"].G, map["Primary"].B)) { failures++; Console.WriteLine($"  mismatch {b.Color} vs {map["Primary"]}"); }
    }
    // Windows accent source must follow accent changes (simulated: stale SourceColor + the accent check the
    // UserPreferenceChanged / wallpaper handlers run). Read-only: the real accent is never changed.
    try
    {
        NotchHub.Core.AppPaths.Initialize(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nh-theme-harness"));
        var settings = new NotchHub.Core.Settings.SettingsService();
        settings.Update(s => s.Appearance.Source = ColorSource.WindowsAccent);
        var svc = new ThemeService(settings, new NoWallpaper());
        await svc.StartAsync();
        var accent = svc.SourceColor;
        Console.WriteLine($"accent source: {accent}");
        typeof(ThemeService).GetProperty("SourceColor")!.SetValue(svc, Color.FromRgb(1, 2, 3));
        typeof(ThemeService).GetMethod("CheckAccent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(svc, null);
        for (var i = 0; i < 40 && svc.SourceColor == Color.FromRgb(1, 2, 3); i++) await Task.Delay(50);
        var okAccent = svc.SourceColor == accent;
        Console.WriteLine($"accent re-check: {svc.SourceColor} expected {accent} -> {(okAccent ? "OK" : "FAIL")}");
        if (!okAccent) failures++;
        svc.Dispose();
    }
    catch (Exception ex) { failures++; Console.WriteLine("accent check: " + ex); }
    app.Shutdown(failures == 0 ? 0 : 1);
});
app.Run();
Console.WriteLine(failures == 0 ? "THEME OK" : $"THEME FAILURES: {failures}");

return failures;
}

sealed class NoWallpaper : NotchHub.Core.Contracts.IWallpaperService
{
    public string? CurrentPath => null;
    public uint? SolidColor => 0xFF336699;
    public event EventHandler? Changed { add { } remove { } }
    public Task<NotchHub.Core.Contracts.WallpaperPixels?> GetPixelsAsync(int maxSize = 128, CancellationToken ct = default) => Task.FromResult<NotchHub.Core.Contracts.WallpaperPixels?>(null);
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Dispose() { }
}
