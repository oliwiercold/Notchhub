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
using NotchHub.Features.Controls;
using NotchHub.UI;
using NotchHub.UI.Animation;
using NotchHub.UI.Localization;
using NotchHub.UI.Modding;
using NotchHub.UI.Theming;

namespace ModsHarness;

/// <summary>Loads the example mods through the real ModLoader into a temp data dir, checks the registry, renders views to PNG.</summary>
public static class Program
{
    private static int _failures;
    private static string _repo = "";
    private static readonly List<string> Huds = [];

    [STAThread]
    public static int Main(string[] args)
    {
        var repo = _repo = FindRepo();
        var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "nh-mods-out"));
        Directory.CreateDirectory(outDir);
        var data = Path.Combine(Path.GetTempPath(), "nh-mods-harness");
        try { if (Directory.Exists(data)) Directory.Delete(data, true); } catch { /* best effort */ }
        AppPaths.Initialize(data);

        // ── install the three examples into <temp>\mods ──
        var mods = AppPaths.Dir("mods");
        CopyDir(Path.Combine(repo, "mods", "examples", "kursy-nbp"), Path.Combine(mods, "kursy-nbp"));
        CopyDir(Path.Combine(repo, "mods", "examples", "system-script"), Path.Combine(mods, "system-script"));
        var pomodoroBin = Directory.GetDirectories(Path.Combine(repo, "mods", "examples", "Pomodoro", "bin", "Debug")).Single();
        CopyDir(pomodoroBin, Path.Combine(mods, "pomodoro"));
        // a broken mod (shows up with an error, must not stop the others)
        Directory.CreateDirectory(Path.Combine(mods, "broken"));
        File.WriteAllText(Path.Combine(mods, "broken", "mod.json"), """{ "id": "broken", "name": "Broken", "kind": "assembly", "assembly": "Missing.dll" }""");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/NotchHub.UI;component/Themes/All.xaml", UriKind.Absolute),
        });
        app.DispatcherUnhandledException += (_, e) => { Fail("UNHANDLED: " + e.Exception); e.Handled = true; };

        AppServices.Ui = new Dispatch(app.Dispatcher);
        AppServices.Settings = new SettingsService();
        AppServices.Shell = new HudShell();
        Motion.ReduceMotion = true;
        DemoServices.LiveEvents = false;
        DemoServices.Install();
        Loc.Register("NotchHub.Features.Controls");
        Loc.SetLanguage("pl");

        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await RunAsync(outDir); }
            catch (Exception ex) { Fail("FAILED: " + ex); }
            finally { app.Shutdown(); }
        });
        app.Run();
        Console.WriteLine(_failures == 0 ? $"ALL OK → {outDir}" : $"{_failures} FAILURE(S) → {outDir}");
        return _failures == 0 ? 0 : 1;
    }

    private static async Task RunAsync(string outDir)
    {
        foreach (var s in AppServices.BackgroundServices()) await s.StartAsync();
        var theme = new ThemeService(AppServices.Settings, AppServices.Wallpaper);
        await theme.StartAsync();

        ModLoader.LoadAll(AppServices.Settings);

        // ── registry checks ──
        foreach (var m in ModRegistry.Mods)
            Console.WriteLine($"mod {m.Id,-14} {m.Kind,-8} enabled={m.IsEnabled} v={m.Version} error={m.Error ?? "-"}");
        Check(ModRegistry.Mods.Count == 4, "4 mods found");
        Check(ModRegistry.Mods.Where(m => m.Id != "broken").All(m => m.Error is null), "examples load without errors");
        Check(ModRegistry.Mods.Single(m => m.Id == "broken").Error is not null, "broken mod reports an error");
        Check(ModRegistry.Mods.Single(m => m.Id == "pomodoro").Kind == ModKind.Assembly, "pomodoro is an assembly mod");
        Check(ModRegistry.Tabs.Select(t => t.Id).OrderBy(x => x).SequenceEqual(["kursy-nbp", "pomodoro", "system-script"]), "3 tabs registered");
        Check(ModRegistry.Tiles.Count == 3, "3 tiles registered");
        var opened = new List<string>();
        ModRegistry.TabOpenRequested += (_, id) => opened.Add(id);

        // ── widget tabs: create, show, wait for data ──
        var views = new Dictionary<string, FrameworkElement>();
        foreach (var tab in ModRegistry.Tabs)
        {
            var v = tab.CreateView();
            views[tab.Id] = v;
            Console.WriteLine($"tab {tab.Id}: {v.GetType().Name} '{tab.Title}' icon={tab.Icon}");
        }
        var windows = new List<Window>();
        var widgets = new StackPanel { Width = 792 };
        widgets.Children.Add(Section(views["kursy-nbp"]));
        widgets.Children.Add(Section(views["system-script"]));
        windows.Add(Host(widgets, out var widgetsHost));
        foreach (var v in views.Values.OfType<ITabView>()) v.OnShown();

        foreach (var id in new[] { "kursy-nbp", "system-script" })
        {
            var ctx = (WidgetDataContext)((FrameworkElement)((ContentControl)views[id]).Content).DataContext;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while ((ctx.Data is null && ctx.Error is null || ctx.IsLoading) && sw.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(200);
            Check(ctx.Data is not null && ctx.Error is null, $"{id}: data loaded ({sw.ElapsedMilliseconds} ms, error={ctx.Error ?? "-"})");
        }
        await Task.Delay(500);
        foreach (var t in ModRegistry.Tiles) Console.WriteLine($"tile {t.Id,-20} '{t.Title}' / '{t.Subtitle}' icon={t.Icon} active={t.IsActive} chevron={t.OnChevron is not null}");
        var nbpTile = ModRegistry.Tiles.Single(t => t.ModId == "kursy-nbp");
        Check(nbpTile.Subtitle is { } s1 && s1.StartsWith("EUR ") && s1.EndsWith(" zł") && s1.Length > 8, "nbp tile subtitle has the EUR rate");
        var sysTile = ModRegistry.Tiles.Single(t => t.ModId == "system-script");
        Check(sysTile.Subtitle is { } s2 && !s2.StartsWith('{') && s2.Contains("RAM"), "system tile subtitle expanded");
        nbpTile.OnClick?.Invoke();
        Check(opened.Contains("kursy-nbp"), "widget tile click requests its tab");
        Save(widgetsHost, Path.Combine(outDir, "widgets.png"));

        // ── templates/widget-template: manifest + view parse (no data source is contacted) ──
        var tplDir = Path.Combine(_repo, "templates", "widget-template");
        var tplManifest = System.Text.Json.JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(Path.Combine(tplDir, "mod.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
        Check(tplManifest is { Id: "moj-widzet", Kind: "widget", Tab: not null, Tile: not null, Data.IntervalSeconds: 300 }, "widget template mod.json parses");
        try
        {
            using var fs = File.OpenRead(Path.Combine(tplDir, "view.xaml"));
            var pc = new System.Windows.Markup.ParserContext();
            pc.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            pc.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
            var tplView = (FrameworkElement)System.Windows.Markup.XamlReader.Load(fs, pc);
            dynamic sample = new System.Dynamic.ExpandoObject();
            sample.value = 21.5; sample.unit = "°C"; sample.fraction = 0.42;
            tplView.DataContext = new WidgetDataContext { Refresh = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }), Data = sample, UpdatedAt = DateTime.Now };
            windows.Add(Host(tplView, out var tplHost));
            await Task.Delay(300);
            Save(tplHost, Path.Combine(outDir, "widget-template.png"));
            Check(true, "widget template view.xaml loads");
        }
        catch (Exception ex) { Fail("widget template view.xaml: " + ex.Message); }

        // ── pomodoro: tile + tab + HUD ──
        var pomoView = views["pomodoro"];
        windows.Add(Host(pomoView, out var pomoHost));
        var pomoTile = ModRegistry.Tiles.Single(t => t.ModId == "pomodoro");
        pomoTile.OnClick?.Invoke(); // start
        await Task.Delay(1300);
        Check(pomoTile.IsActive && pomoTile.Subtitle?.Contains("24:5") == true, $"pomodoro running: '{pomoTile.Subtitle}'");
        var mod = FindPomodoro();
        // jump to the end of the work phase → HUD + automatic break
        mod.GetType().GetField("_endsAt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(mod, DateTime.UtcNow.AddSeconds(-1));
        mod.GetType().GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mod, null);
        Check(Huds.Any(h => h.Contains("przerwy")), $"phase end shows a HUD ({string.Join(" | ", Huds)})");
        Check(pomoTile.Subtitle?.StartsWith("Przerwa") == true && pomoTile.Icon == "coffee", $"break started: '{pomoTile.Subtitle}'");
        pomoTile.OnClick?.Invoke(); // pause
        Check(!pomoTile.IsActive, "tile click pauses");
        pomoTile.OnChevron?.Invoke();
        Check(opened.Contains("pomodoro"), "chevron requests the pomodoro tab");
        mod.GetType().GetMethod("Reset")!.Invoke(mod, null);
        mod.GetType().GetMethod("Start")!.Invoke(mod, null);
        await Task.Delay(1200);
        Save(pomoHost, Path.Combine(outDir, "pomodoro.png"));

        // ── Controls tab with the Mods row ──
        var controls = new ControlsView();
        windows.Add(Host(controls, out var controlsHost));
        controls.OnShown();
        await Task.Delay(1200);
        var row = (ItemsControl)controls.FindName("ModTiles");
        Check(row.Visibility == Visibility.Visible && row.Items.Count == 3, "controls: mods row visible with 3 tiles");
        Save(controlsHost, Path.Combine(outDir, "controls.png"));
        // live update: removing tiles hides the row
        var saved = ModRegistry.Tiles.ToList();
        ModRegistry.Tiles.Clear();
        controlsHost.UpdateLayout();
        Check(row.Visibility == Visibility.Collapsed, "controls: mods row hidden without tiles");
        foreach (var t in saved) ModRegistry.Tiles.Add(t);
        controlsHost.UpdateLayout();
        Check(row.Visibility == Visibility.Visible, "controls: mods row back after tiles re-added");
        controls.OnHidden();

        foreach (var v in views.Values.OfType<ITabView>()) v.OnHidden();

        await XamlSafetyChecksAsync();
        await CommandChecksAsync();

        ModLoader.ShutdownAll();
        foreach (var w in windows) w.Close();
        theme.Dispose();
    }

    // ── widget view.xaml goes through the restricted loader (WidgetXaml) ──
    private static Task XamlSafetyChecksAsync()
    {
        var wx = typeof(ModLoader).Assembly.GetType("NotchHub.UI.Modding.WidgetXaml", true)!;
        var load = wx.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;
        var dir = Path.Combine(Path.GetTempPath(), "nh-xaml-safety");
        Directory.CreateDirectory(dir);
        var marker = Path.Combine(dir, "pwned.txt");
        try { File.Delete(marker); } catch { }

        foreach (var ok in new[]
                 {
                     Path.Combine(_repo, "mods", "examples", "kursy-nbp", "view.xaml"),
                     Path.Combine(_repo, "mods", "examples", "system-script", "view.xaml"),
                     Path.Combine(_repo, "templates", "widget-template", "view.xaml"),
                 })
        {
            try { Check(load.Invoke(null, [ok]) is FrameworkElement, $"safe loader accepts {Path.GetFileName(Path.GetDirectoryName(ok))}"); }
            catch (TargetInvocationException ex) { Fail($"safe loader rejected {ok}: {ex.InnerException?.Message}"); }
        }

        const string Head = """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" """;
        var bad = new Dictionary<string, string>
        {
            ["ObjectDataProvider + System.IO"] = Head + """xmlns:io="clr-namespace:System.IO;assembly=System.Runtime"><Grid.Resources><ObjectDataProvider x:Key="p" ObjectType="{x:Type io:File}" MethodName="WriteAllText"><ObjectDataProvider.MethodParameters><x:Static Member="x:Null"/></ObjectDataProvider.MethodParameters></ObjectDataProvider></Grid.Resources></Grid>""",
            ["ObjectDataProvider (presentation ns only)"] = Head + """><Grid.Resources><ObjectDataProvider x:Key="p" ObjectType="{x:Type TextBlock}" MethodName="ToString"/></Grid.Resources></Grid>""",
            ["System.Diagnostics xmlns"] = Head + """xmlns:d="clr-namespace:System.Diagnostics;assembly=System.Diagnostics.Process"><TextBlock Text="{x:Static d:Process.ProcessName}"/></Grid>""",
            ["XmlDataProvider"] = Head + """><Grid.Resources><XmlDataProvider x:Key="x" Source="http://127.0.0.1:9/x.xml"/></Grid.Resources></Grid>""",
            ["event handler"] = Head + """><Button Click="OnClick"/></Grid>""",
            ["EventSetter"] = Head + """><Grid.Resources><Style TargetType="Button"><EventSetter Event="Click" Handler="OnClick"/></Style></Grid.Resources></Grid>""",
            ["x:Code"] = Head + """><x:Code><![CDATA[ void F() { } ]]></x:Code></Grid>""",
            ["x:Class"] = Head.Replace("<Grid ", "<Grid x:Class=\"Evil.View\" ") + "></Grid>",
            ["ResourceDictionary.Source"] = Head + """><Grid.Resources><ResourceDictionary Source="other.xaml"/></Grid.Resources></Grid>""",
            ["Frame"] = Head + """><Frame Source="http://127.0.0.1:9/"/></Grid>""",
            ["Hyperlink.NavigateUri"] = Head + """><TextBlock><Hyperlink NavigateUri="file:///C:/Windows/notepad.exe">x</Hyperlink></TextBlock></Grid>""",
            ["NotchHub.UI.Modding xmlns"] = Head + """xmlns:m="clr-namespace:NotchHub.UI.Modding;assembly=NotchHub.UI"><TextBlock Text="{x:Static m:ModLoader.ModFolders}"/></Grid>""",
            ["sys xmlns"] = Head + """xmlns:sys="clr-namespace:System;assembly=System.Runtime"><TextBlock Text="{x:Static sys:Environment.UserName}"/></Grid>""",
            ["DTD"] = """<!DOCTYPE Grid [ <!ENTITY e "x"> ]>""" + Head + "></Grid>",
        };
        foreach (var (name, xaml) in bad)
        {
            var file = Path.Combine(dir, "view.xaml");
            File.WriteAllText(file, xaml);
            try
            {
                load.Invoke(null, [file]);
                Fail($"safe loader ACCEPTED unsafe view: {name}");
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException!;
                Check(inner is InvalidDataException or System.Xml.XmlException, $"safe loader rejects {name}: {inner.GetType().Name}: {inner.Message}");
            }
        }
        Check(!File.Exists(marker), "no side effect from rejected views");
        return Task.CompletedTask;
    }

    // ── command widgets: stderr flood must not deadlock, a hung command is killed after 20 s ──
    private static async Task CommandChecksAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nh-cmd-checks");
        Directory.CreateDirectory(dir);
        var pidFile = Path.Combine(dir, "pid.txt");
        try { File.Delete(pidFile); } catch { }
        var widgetType = typeof(ModLoader).Assembly.GetType("NotchHub.UI.Modding.WidgetMod", true)!;
        var ctxType = typeof(ModLoader).Assembly.GetType("NotchHub.UI.Modding.ModContext", true)!;

        async Task<WidgetDataContext> Run(string id, string script, int waitSeconds)
        {
            var m = new ModManifest
            {
                Id = id, Name = id, Kind = "widget",
                Tile = new ManifestTile { Title = id },
                Data = new ManifestData { Command = "powershell.exe", Args = ["-NoProfile", "-NonInteractive", "-Command", script], IntervalSeconds = 3600 },
            };
            var ctx = Activator.CreateInstance(ctxType, id, dir)!;
            var w = Activator.CreateInstance(widgetType, BindingFlags.Public | BindingFlags.Instance, null, [m, dir, ctx, AppServices.Settings], null)!;
            var dc = (WidgetDataContext)widgetType.GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(300);
            while (dc.IsLoading && sw.Elapsed < TimeSpan.FromSeconds(waitSeconds)) await Task.Delay(200);
            ((IDisposable)w).Dispose();
            Console.WriteLine($"cmd {id}: {sw.ElapsedMilliseconds} ms data={(dc.Data is null ? "-" : "yes")} error={dc.Error ?? "-"}");
            return dc;
        }

        var flood = await Run("stderr-flood", "[Console]::Error.Write('e' * 300000); [Console]::Out.Write('{\"ok\":1}')", 15);
        Check(flood.Data is not null && flood.Error is null && !flood.IsLoading, "command writing 300 KB to stderr still returns its JSON");

        var hang = await Run("hang", $"$PID | Set-Content -LiteralPath '{pidFile}'; Start-Sleep -Seconds 90", 30);
        Check(hang.Error is { } e && e.Contains("20 s") && !hang.IsLoading, $"hung command times out ({hang.Error})");
        await Task.Delay(500);
        if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid))
        {
            bool alive;
            try { using var p = System.Diagnostics.Process.GetProcessById(pid); alive = !p.HasExited; }
            catch (ArgumentException) { alive = false; }
            Check(!alive, $"hung command process {pid} was killed");
        }
        else Fail("hang test: pid file not written");
        foreach (var t in ModRegistry.Tiles.Where(t => t.ModId is "stderr-flood" or "hang").ToList()) ModRegistry.Tiles.Remove(t);
    }

    private static object FindPomodoro()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Pomodoro");
        // the mod instance is held by the tile's click delegate target
        var tile = ModRegistry.Tiles.Single(t => t.ModId == "pomodoro");
        var target = tile.OnClick!.Target!;
        Check(target.GetType().Assembly == asm, "pomodoro instance found");
        return target;
    }

    private static FrameworkElement Section(FrameworkElement view) => new Border { Margin = new Thickness(0, 0, 0, 24), Child = view };

    private static Window Host(FrameworkElement view, out Border host)
    {
        host = new Border { Padding = new Thickness(14), Width = 792 + 28, CornerRadius = new CornerRadius(0, 0, 32, 32), Child = view };
        host.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight, Content = host,
        };
        window.SetResourceReference(Control.FontFamilyProperty, "Font.Sans");
        window.Show();
        return window;
    }

    private static void Save(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var w = (int)Math.Ceiling(element.ActualWidth);
        var h = (int)Math.Ceiling(element.ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x55)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        rtb.Render(dv);
        using var fs = File.Create(path);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        enc.Save(fs);
        Console.WriteLine($"saved {path} ({w}x{h})");
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) _failures++;
    }

    private static void Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        _failures++;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NotchHub.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
    }

    private sealed class HudShell : IShell
    {
        public ShellState State => ShellState.Expanded;
        public ShellTab ActiveTab => ShellTab.Controls;
        public event EventHandler? StateChanged { add { } remove { } }
        public void Expand(ShellTab? tab = null, bool activate = true) { }
        public void Collapse() { }
        public void Toggle(ShellTab? tab = null) { }
        public void ShowHud(HudRequest request)
        {
            Huds.Add($"{request.Payload}");
            Console.WriteLine($"[hud] {request.Kind}: {request.Payload}");
        }
        public IDisposable HoldOpen() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class Dispatch(Dispatcher d) : IUiDispatcher
    {
        public bool CheckAccess() => d.CheckAccess();
        public void Post(Action action) => d.BeginInvoke(action);
        public Task InvokeAsync(Action action) => d.InvokeAsync(action).Task;
        public Task<T> InvokeAsync<T>(Func<T> func) => d.InvokeAsync(func).Task;
    }
}
