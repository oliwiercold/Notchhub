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
    app.Shutdown(failures == 0 ? 0 : 1);
});
app.Run();
Console.WriteLine(failures == 0 ? "THEME OK" : $"THEME FAILURES: {failures}");

return failures;
}
