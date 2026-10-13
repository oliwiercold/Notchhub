using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Demo;
using NotchHub.UI;
using NotchHub.UI.Animation;
using NotchHub.UI.Localization;
using NotchHub.UI.Theming;

namespace NotchHub.Preview;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PreviewFeatureAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}

/// <summary>
/// Renders every view (UserControl implementing ITabView) of the compiled-in feature assembly to PNG, using demo services and the
/// real theme pipeline (demo wallpaper → gold scheme). Optional per-feature states: if the feature assembly contains a class
/// named "PreviewStates" (any visibility) with static methods
///     IEnumerable&lt;string&gt; Names(FrameworkElement view)
///     Task ApplyAsync(FrameworkElement view, string state)
/// each named state is rendered as an extra PNG (e.g. "sheet-open", "item-selected").
/// HudView-like views whose ViewModel has Present(HudRequest) are rendered once per HUD kind.
/// Args: --out dir  --lang pl|en  --width 792  --wait 1200 (ms after OnShown)  --scale2x
/// </summary>
public static class Program
{
    private static int _exitCode;

    [STAThread]
    public static int Main(string[] args)
    {
        var feature = typeof(Program).Assembly.GetCustomAttribute<PreviewFeatureAttribute>()!.Feature;
        var outDir = Path.GetFullPath(Arg(args, "--out") ?? Path.Combine("screenshots", "preview", feature.ToLowerInvariant()));
        var lang = Arg(args, "--lang") ?? "pl";
        var width = double.Parse(Arg(args, "--width") ?? "792", CultureInfo.InvariantCulture);
        var waitMs = int.Parse(Arg(args, "--wait") ?? "1200", CultureInfo.InvariantCulture);
        var scale2x = args.Contains("--scale2x");
        Directory.CreateDirectory(outDir);

        var data = Path.Combine(Path.GetTempPath(), "nh-preview-" + feature.ToLowerInvariant());
        try { if (Directory.Exists(data)) Directory.Delete(data, true); } catch { /* best effort */ }
        AppPaths.Initialize(data);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/NotchHub.UI;component/Themes/All.xaml", UriKind.Absolute),
        });
        app.DispatcherUnhandledException += (_, e) =>
        {
            Console.Error.WriteLine("UNHANDLED: " + e.Exception);
            Log.Error("Preview unhandled", e.Exception);
            _exitCode = 2;
            e.Handled = true;
        };

        AppServices.Ui = new Dispatch(app.Dispatcher);
        AppServices.Settings = new SettingsService();
        AppServices.Settings.Update(s => s.Language = lang);
        AppServices.Shell = new NullShell();
        Motion.ReduceMotion = true;
        DemoServices.LiveEvents = false;
        DemoServices.Install();
        Loc.Register($"NotchHub.Features.{feature}");
        Loc.SetLanguage(lang);

        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await RunAsync(feature, outDir, width, waitMs, scale2x); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAILED: " + ex);
                _exitCode = 1;
            }
            finally { app.Shutdown(); }
        });
        app.Run();
        Console.WriteLine($"done (exit {_exitCode}) → {outDir}");
        return _exitCode;
    }

    private static async Task RunAsync(string feature, string outDir, double width, int waitMs, bool scale2x)
    {
        foreach (var s in AppServices.BackgroundServices())
            await s.StartAsync();
        var theme = new ThemeService(AppServices.Settings, AppServices.Wallpaper);
        await theme.StartAsync();
        await Task.Delay(400);

        var asm = Assembly.Load($"NotchHub.Features.{feature}");
        var viewTypes = asm.GetTypes()
            .Where(t => typeof(FrameworkElement).IsAssignableFrom(t) && typeof(ITabView).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.Name)
            .ToList();
        var states = asm.GetTypes().FirstOrDefault(t => t.Name == "PreviewStates");

        foreach (var type in viewTypes)
        {
            var view = (FrameworkElement)Activator.CreateInstance(type)!;
            var host = new Border
            {
                Padding = new Thickness(14),
                Width = width + 28,
                CornerRadius = new CornerRadius(0, 0, 32, 32),
                Child = view,
            };
            host.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            var window = new Window
            {
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight,
                Content = host,
            };
            window.SetResourceReference(Control.FontFamilyProperty, "Font.Sans");
            window.Show();
            ((ITabView)view).OnShown();
            await Task.Delay(waitMs);

            var name = type.Name.Replace("View", "").ToLowerInvariant();
            var present = view.GetType().GetProperty("ViewModel")?.GetValue(view) is { } vm
                ? vm.GetType().GetMethod("Present", [typeof(HudRequest)]) : null;
            if (present is not null)
            {
                var vm2 = view.GetType().GetProperty("ViewModel")!.GetValue(view)!;
                foreach (var req in SampleHuds())
                {
                    present.Invoke(vm2, [req]);
                    await Task.Delay(500);
                    Save(host, Path.Combine(outDir, $"{name}-{req.Kind.ToString().ToLowerInvariant()}.png"), scale2x);
                }
            }
            else
            {
                Save(host, Path.Combine(outDir, $"{name}.png"), scale2x);
            }

            if (states is not null)
            {
                var names = states.GetMethod("Names", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(null, [view]) as IEnumerable<string> ?? [];
                var apply = states.GetMethod("ApplyAsync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var state in names.ToList())
                {
                    if (apply?.Invoke(null, [view, state]) is Task t) await t;
                    await Task.Delay(Math.Max(400, waitMs / 2));
                    Save(host, Path.Combine(outDir, $"{name}-{state}.png"), scale2x);
                }
            }

            ((ITabView)view).OnHidden();
            window.Close();
        }
        theme.Dispose();
    }

    private static IEnumerable<HudRequest> SampleHuds()
    {
        yield return new HudRequest(HudKind.Volume, new HudLevel(0.64));
        yield return new HudRequest(HudKind.Brightness, new HudLevel(0.7));
        var n = AppServices.Phone.Notifications.FirstOrDefault();
        if (n is not null) yield return new HudRequest(HudKind.PhoneNotification, n);
        var d = AppServices.Phone.ActiveDevice;
        if (d is not null) yield return new HudRequest(HudKind.PhonePairRequest, d with { PairState = PhonePairState.RequestedByPeer, VerificationKey = "8F3A21C4" });
        yield return new HudRequest(HudKind.TimerFinished);
        yield return new HudRequest(HudKind.Info, "Skopiowano do schowka");
    }

    private static void Save(FrameworkElement element, string path, bool scale2x)
    {
        element.UpdateLayout();
        foreach (var scale in scale2x ? new[] { 1.0, 2.0 } : [1.0])
        {
            var w = (int)Math.Ceiling(element.ActualWidth * scale);
            var h = (int)Math.Ceiling(element.ActualHeight * scale);
            if (w <= 0 || h <= 0) { Console.Error.WriteLine($"empty element for {path}"); _exitCode = 1; return; }
            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            // render on top of a dark backdrop so the notch edges are visible
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x55)), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            rtb.Render(dv);
            var file = scale > 1 ? path.Replace(".png", "@2x.png") : path;
            using var fs = File.Create(file);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            enc.Save(fs);
            Console.WriteLine($"saved {file} ({w}x{h})");
        }
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private sealed class Dispatch(Dispatcher d) : IUiDispatcher
    {
        public bool CheckAccess() => d.CheckAccess();
        public void Post(Action action) => d.BeginInvoke(action);
        public Task InvokeAsync(Action action) => d.CheckAccess() ? Run(action) : d.InvokeAsync(action).Task;
        public Task<T> InvokeAsync<T>(Func<T> func) => d.CheckAccess() ? Task.FromResult(func()) : d.InvokeAsync(func).Task;
        private static Task Run(Action a) { a(); return Task.CompletedTask; }
    }

    private sealed class NullShell : IShell
    {
        public ShellState State => ShellState.Expanded;
        public ShellTab ActiveTab => ShellTab.Controls;
        public event EventHandler? StateChanged { add { } remove { } }
        public void Expand(ShellTab? tab = null, bool activate = true) => Console.WriteLine($"[shell] Expand({tab})");
        public void Collapse() => Console.WriteLine("[shell] Collapse()");
        public void Toggle(ShellTab? tab = null) => Console.WriteLine($"[shell] Toggle({tab})");
        public void ShowHud(HudRequest request) => Console.WriteLine($"[shell] ShowHud({request.Kind})");
        public IDisposable HoldOpen() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
