using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotchHub.Core.Settings;
using NotchHub.Installer.Engine;
using NotchHub.Installer.Ui;
using NotchHub.UI.Animation;
using NotchHub.UI.Localization;
using NotchHub.UI.Theming;

namespace NotchHub.Installer;

/// <summary>
/// The setup UI: a black notch at the top-center of the screen that springs open into a compact card
/// (prompt → options → progress → done; or the uninstall flow). Esc cancels until the installation starts.
/// </summary>
public partial class SetupWindow : Window
{
    private static readonly Color Gold = Color.FromRgb(0xD4, 0xAF, 0x5A);
    private const double CollapsedW = 200, CollapsedH = 12;

    private readonly SetupContext _ctx;
    private readonly SetupOptions _options = new();
    private FrameworkElement? _page;
    private bool _busy, _closing, _finished;
    private DispatcherTimer? _autoLaunch;
    private Action? _afterRunningConfirm;

    public SetupWindow(SetupContext ctx)
    {
        _ctx = ctx;
        InitializeComponent();
        DataContext = _options;

        _options.Language = Loc.Language;
        if (ctx.NoLaunchPreset) _options.LaunchAfter = false;
        if (ctx.ExistingDir is not null) _options.InstallDir = ctx.ExistingDir;
        else if (ctx.TargetOverride is not null) _options.InstallDir = ctx.TargetOverride;
        if (ctx.ExistingDir is not null && !ctx.DryRun)
        {
            // upgrade: preselect what the user has now
            _options.DesktopShortcut = File.Exists(ShortcutPaths.Desktop);
            _options.StartMenuShortcut = File.Exists(ShortcutPaths.StartMenu);
        }
        if (ctx.ExistingDir is not null)
        {
            FolderBox.IsEnabled = false;
            BrowseButton.IsEnabled = false;
        }
        var settingsKept = ctx.SettingsExist;
        SettingsSections.Visibility = settingsKept ? Visibility.Collapsed : Visibility.Visible;
        KeptNote.Visibility = settingsKept ? Visibility.Visible : Visibility.Collapsed;
        var dry = ctx.DryRun ? Visibility.Visible : Visibility.Collapsed;
        DryBadgeOptions.Visibility = dry;
        DryBadgeUninstall.Visibility = dry;

        var icon = LoadAppIcon();
        PromptIcon.Source = icon;
        UninstallIcon.Source = icon;
        BuildSwatches();
        RemoveData.IsChecked = ctx.RemoveDataPreset;

        _options.PropertyChanged += OnOptionChanged;
        Loc.LanguageChanged += (_, _) => RefreshTexts();
        RefreshTexts();
        ApplyTheme(animate: false);

        Notch.BodyWidth = CollapsedW;
        Notch.BodyHeight = CollapsedH;
        Body.Opacity = 0;
        Loaded += OnLoaded;
        PreviewKeyDown += OnKey;
    }

    // ───────────────────────────── window ─────────────────────────────

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var screenW = SystemParameters.PrimaryScreenWidth;
        Height = Math.Min(Height, SystemParameters.PrimaryScreenHeight - 40);
        PageOptions.Height = Math.Min(PageOptions.Height, Height - 24);
        Left = Math.Round((screenW - Width) / 2);
        Top = 0;
        Activate();
        Focus();

        // the collapsed notch appears first, then springs open into the card
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Start();
        };
        timer.Start();

        if (_ctx.AutoCloseSeconds > 0)
        {
            var auto = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_ctx.AutoCloseSeconds) };
            auto.Tick += (_, _) =>
            {
                auto.Stop();
                SetupLog.Info("--auto-close elapsed");
                CloseAnimated();
            };
            auto.Start();
        }
    }

    private void Start()
    {
        if (_ctx.Uninstall)
        {
            var dir = _ctx.UninstallDir();
            if (dir is null || !File.Exists(Path.Combine(dir, SetupContext.AppExe)))
            {
                ShowError(Loc.Get("setup.error.notInstalled"), retry: false);
                return;
            }
            if (_ctx.StartPage == "done") BeginUninstall();
            else Show(PageUninstall);
            return;
        }
        switch (_ctx.StartPage)
        {
            case "options": Show(PageOptions); break;
            case "options-end":
                Show(PageOptions);
                Dispatcher.BeginInvoke(OptionsScroll.ScrollToEnd, DispatcherPriority.Loaded);
                break;
            case "done": OnInstall(this, new RoutedEventArgs()); break;
            default: Show(PagePrompt); break;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _busy) return;
        e.Handled = true;
        if (_page == PageOptions && _ctx.StartPage is not ("options" or "options-end")) Show(PagePrompt);
        else CloseAnimated();
    }

    /// <summary>
    /// The card morphs back into the plain collapsed notch (same size and place as the app's own notch) and fades out.
    /// With <paramref name="launch"/> the app is started only after the setup window is gone, so its notch takes over
    /// in the same spot instead of appearing above the setup card.
    /// </summary>
    private void CloseAnimated(bool launch = false)
    {
        if (_closing) return;
        _closing = true;
        _autoLaunch?.Stop();
        _autoLaunch = null;
        Body.Animate(OpacityProperty, 0, MotionToken.FxFast);
        Notch.Animate(SetupNotch.OutlineOpacityProperty, 0, MotionToken.FxFast);
        Notch.Animate(SetupNotch.BodyHeightProperty, CollapsedH, MotionToken.Shrink);
        Notch.Animate(SetupNotch.RadiusProperty, 9, MotionToken.Shrink);
        Notch.Animate(SetupNotch.BodyWidthProperty, CollapsedW, MotionToken.ShrinkWide, () =>
        {
            this.Animate(OpacityProperty, 0, MotionToken.Fx, () => Finish(launch));
        });
        // safety net if a spring never settles (e.g. window hidden)
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            Finish(launch);
        };
        t.Start();
    }

    private void Finish(bool launch)
    {
        if (_finished) return;
        _finished = true;
        Hide();
        if (launch) new InstallEngine(_ctx, _options).Launch();
        Application.Current.Shutdown(0);
    }

    // ───────────────────────────── pages ─────────────────────────────

    private void Show(FrameworkElement page)
    {
        if (_page == page) return;
        foreach (var p in new FrameworkElement[] { PagePrompt, PageOptions, PageRunning, PageProgress, PageDone, PageError, PageUninstall })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        DryBadgePrompt.Visibility = _ctx.DryRun && page == PagePrompt ? Visibility.Visible : Visibility.Collapsed;
        var first = _page is null;
        _page = page;

        var grow = page.Height > Notch.BodyHeight;
        Notch.Animate(SetupNotch.BodyWidthProperty, page.Width, grow ? MotionToken.GrowWide : MotionToken.ShrinkWide);
        Notch.Animate(SetupNotch.BodyHeightProperty, page.Height, grow ? MotionToken.Grow : MotionToken.Shrink);
        Notch.Animate(SetupNotch.RadiusProperty, 30, MotionToken.Small);
        Notch.Animate(SetupNotch.ShoulderProperty, 10, MotionToken.Small);
        Notch.Animate(SetupNotch.OutlineOpacityProperty, 0.28, MotionToken.FxSlow);

        // content: quick fade + a small drop, like the app's panel content
        Body.Set(OpacityProperty, 0);
        ContentShift.Set(TranslateTransform.YProperty, first ? -14 : -6);
        Body.Animate(OpacityProperty, 1, Motion.SpringOf(MotionToken.Fx), first ? 0.08 : 0.04);
        ContentShift.Animate(TranslateTransform.YProperty, 0, MotionToken.ContentIn);

        if (page == PageDone && !Motion.ReduceMotion)
        {
            DoneScale.Set(ScaleTransform.ScaleXProperty, 0.4);
            DoneScale.Set(ScaleTransform.ScaleYProperty, 0.4);
            DoneScale.Animate(ScaleTransform.ScaleXProperty, 1, Motion.SpringOf(MotionToken.Pop), 0.12);
            DoneScale.Animate(ScaleTransform.ScaleYProperty, 1, Motion.SpringOf(MotionToken.Pop), 0.12);
        }
        (page == PageOptions ? (IInputElement)InstallButton2 : page == PagePrompt ? InstallButton : this).Focus();
    }

    private void RefreshTexts()
    {
        var upgrade = _ctx.ExistingDir is not null;
        var same = upgrade && _ctx.ExistingVersion == _ctx.Version;
        var mode = same ? "reinstall" : upgrade ? "upgrade" : "install";
        PromptTitle.Text = Loc.Get("setup.title." + mode);
        PromptSub.Text = mode switch
        {
            "upgrade" => Loc.Format("setup.sub.upgrade", _ctx.ExistingVersion ?? "?", _ctx.Version),
            _ => Loc.Format("setup.sub." + mode, _ctx.Version),
        };
        InstallButton.Content = Loc.Get("setup." + mode);
        InstallButton2.Content = InstallButton.Content;
        ProgressTitle.Text = Loc.Get(_ctx.Uninstall ? "setup.progress.uninstall" : "setup.progress.install");
        ErrorLog.Text = Loc.Format("setup.error.sub", SetupLog.FilePath);
        if (_page == PageDone) FillDone();
    }

    // ───────────────────────────── options ─────────────────────────────

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SetupOptions.Source) or nameof(SetupOptions.CustomColor) or nameof(SetupOptions.Variant):
                ApplyTheme(animate: true);
                break;
            case nameof(SetupOptions.Animation):
                Motion.Profile = _options.Animation switch
                {
                    AnimationStyle.Legacy => MotionProfile.Legacy,
                    AnimationStyle.Smooth => MotionProfile.Smooth,
                    _ => MotionProfile.Refreshed,
                };
                break;
        }
    }

    /// <summary>Live preview of the chosen colors (wallpaper sources preview with the default gold).</summary>
    private void ApplyTheme(bool animate)
    {
        var source = _options.Source switch
        {
            ColorSource.WindowsAccent => WindowsAccent() ?? Gold,
            ColorSource.Custom => ParseColor(_options.CustomColor) ?? Gold,
            _ => Gold,
        };
        ThemeService.Apply(ThemeService.Generate(source, _options.Variant, 0, PanelStyle.Black), animate);
    }

    private void BuildSwatches()
    {
        foreach (var hex in SetupOptions.Swatches)
        {
            var color = ParseColor(hex) ?? Gold;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var rb = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                GroupName = "swatch",
                Background = brush,
                ToolTip = hex,
                IsChecked = string.Equals(hex, _options.CustomColor, StringComparison.OrdinalIgnoreCase),
            };
            rb.Checked += (_, _) => _options.CustomColor = hex;
            SwatchPanel.Children.Add(rb);
        }
    }

    private void OnLangPl(object sender, RoutedEventArgs e) => SetLanguage("pl");
    private void OnLangEn(object sender, RoutedEventArgs e) => SetLanguage("en");

    private void SetLanguage(string lang)
    {
        if (_options.Language == lang && Loc.Language == lang) return;
        _options.Language = lang;
        Loc.SetLanguage(lang);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(_options.InstallDir)) ? Path.GetDirectoryName(_options.InstallDir) : null,
        };
        Topmost = false;
        try
        {
            if (dialog.ShowDialog(this) != true) return;
        }
        finally
        {
            Topmost = true;
        }
        var dir = dialog.FolderName;
        // never install straight into a folder with other content (uninstall must not touch the user's files)
        if (!string.Equals(Path.GetFileName(dir.TrimEnd('\\')), "NotchHub", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            dir = Path.Combine(dir, "NotchHub");
        _options.InstallDir = dir;
    }

    // ───────────────────────────── install ─────────────────────────────

    private void OnOptions(object sender, RoutedEventArgs e) => Show(PageOptions);
    private void OnBack(object sender, RoutedEventArgs e) => Show(PagePrompt);
    private void OnCancel(object sender, RoutedEventArgs e) => CloseAnimated();

    private void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!InstallEngine.HasPayload)
        {
            ShowError(Loc.Get("setup.error.payload"), retry: false);
            return;
        }
        if (!IsValidFolder(_options.InstallDir))
        {
            Show(PageOptions);
            FolderBox.Focus();
            return;
        }
        var scope = _ctx.DryRun ? _options.InstallDir : null;
        if (AppProcesses.AnyRunning(scope))
        {
            _afterRunningConfirm = RunInstall;
            Show(PageRunning);
            return;
        }
        RunInstall();
    }

    private async void RunInstall()
    {
        _busy = true;
        Show(PageProgress);
        Wave.Set(NotchHub.UI.Controls.WavyProgressBar.ValueProperty, 0);
        var engine = new InstallEngine(_ctx, _options);
        try
        {
            var progress = new Progress<SetupProgress>(OnProgress);
            var started = DateTime.UtcNow;
            await Task.Run(() => engine.Run(progress));
            // let the wave be seen for a moment even when the copy is instant
            var left = TimeSpan.FromMilliseconds(900) - (DateTime.UtcNow - started);
            if (left > TimeSpan.Zero) await Task.Delay(left);
            _busy = false;
            FillDone();
            Show(PageDone);
            if (_options.LaunchAfter)
            {
                // a short moment on "Gotowe!", then the card folds back into the notch and the app takes over
                _autoLaunch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
                _autoLaunch.Tick += (_, _) => CloseAnimated(launch: true);
                _autoLaunch.Start();
            }
        }
        catch (Exception ex)
        {
            SetupLog.Error("Install failed", ex);
            _busy = false;
            ShowError(ex.Message, retry: true);
        }
    }

    private void OnProgress(SetupProgress p)
    {
        ProgressStep.Text = Loc.Get(p.StepKey);
        Wave.Animate(NotchHub.UI.Controls.WavyProgressBar.ValueProperty, p.Value, MotionToken.Data);
    }

    private void FillDone()
    {
        if (_ctx.Uninstall)
        {
            DoneIcon.Glyph = "waving_hand";
            DoneTitle.Text = Loc.Get("setup.done.uninstall");
            DoneSub.Text = Loc.Get("setup.done.uninstallSub");
            LaunchButton.Visibility = Visibility.Collapsed;
            return;
        }
        DoneIcon.Glyph = "check";
        DoneTitle.Text = Loc.Get("setup.done.title");
        DoneSub.Text = Loc.Get(_options.LaunchAfter ? "setup.done.subLaunch" : "setup.done.sub");
        LaunchButton.Visibility = Visibility.Visible;
    }

    private void OnLaunch(object sender, RoutedEventArgs e) => CloseAnimated(launch: true);

    private void OnRunningContinue(object sender, RoutedEventArgs e)
    {
        var next = _afterRunningConfirm;
        _afterRunningConfirm = null;
        next?.Invoke();
    }

    private void OnRunningCancel(object sender, RoutedEventArgs e)
    {
        _afterRunningConfirm = null;
        if (_ctx.Uninstall) Show(PageUninstall);
        else Show(PagePrompt);
    }

    private Action? _retry;

    private void ShowError(string message, bool retry)
    {
        ErrorText.Text = message;
        ErrorText.ToolTip = message;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        _retry = retry ? (_ctx.Uninstall ? BeginUninstall : RunInstall) : null;
        Show(PageError);
    }

    private void OnRetry(object sender, RoutedEventArgs e) => _retry?.Invoke();

    // ───────────────────────────── uninstall ─────────────────────────────

    private void OnUninstall(object sender, RoutedEventArgs e) => BeginUninstall();

    private void BeginUninstall()
    {
        if (_busy) return;
        var dir = _ctx.UninstallDir()!;
        if (AppProcesses.AnyRunning(_ctx.DryRun ? dir : null) && _page != PageRunning)
        {
            _afterRunningConfirm = RunUninstall;
            Show(PageRunning);
            return;
        }
        RunUninstall();
    }

    private async void RunUninstall()
    {
        _busy = true;
        Show(PageProgress);
        Wave.Set(NotchHub.UI.Controls.WavyProgressBar.ValueProperty, 0);
        var engine = new UninstallEngine(_ctx, _ctx.UninstallDir()!, RemoveData.IsChecked == true);
        try
        {
            var started = DateTime.UtcNow;
            var progress = new Progress<SetupProgress>(OnProgress); // created on the UI thread → reports marshal back to it
            await Task.Run(() => engine.Run(progress));
            var left = TimeSpan.FromMilliseconds(900) - (DateTime.UtcNow - started);
            if (left > TimeSpan.Zero) await Task.Delay(left);
            _busy = false;
            FillDone();
            Show(PageDone);
        }
        catch (Exception ex)
        {
            SetupLog.Error("Uninstall failed", ex);
            _busy = false;
            ShowError(ex.Message, retry: true);
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private static bool IsValidFolder(string dir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathFullyQualified(dir)) return false;
            var full = Path.GetFullPath(dir).TrimEnd('\\');
            var root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\');
            return !string.Equals(full, root, StringComparison.OrdinalIgnoreCase); // never a drive root
        }
        catch
        {
            return false;
        }
    }

    private static ImageSource? LoadAppIcon()
    {
        try
        {
            using var s = typeof(SetupWindow).Assembly.GetManifestResourceStream("NotchHub.Payload.app.ico");
            if (s is null) return null;
            var decoder = new IconBitmapDecoder(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            SetupLog.Warn("App icon not loaded", ex);
            return null;
        }
    }

    private static Color? WindowsAccent()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr) return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
        }
        catch
        {
            // fall through
        }
        return null;
    }

    private static Color? ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return null; }
    }
}
