using System.Windows;
using NotchHub.Core;
using NotchHub.Installer.Engine;
using NotchHub.UI.Animation;
using NotchHub.UI.Localization;

namespace NotchHub.Installer;

/// <summary>NotchHub setup / uninstaller (see <see cref="SetupContext"/> for the command line).</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // NotchHub.Core / NotchHub.UI log through AppPaths: keep that away from the user's real data folder (which the
        // uninstaller may be deleting) — setup's own log is %TEMP%\NotchHub-setup.log.
        AppPaths.Initialize(Path.Combine(Path.GetTempPath(), "NotchHub-setup-core"));
        DispatcherUnhandledException += (_, args) =>
        {
            SetupLog.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            SetupLog.Error("Unhandled exception (fatal)", args.ExceptionObject as Exception);

        SetupContext ctx;
        try
        {
            ctx = SetupContext.Parse(e.Args);
        }
        catch (Exception ex)
        {
            SetupLog.Error("Bad command line", ex);
            MessageBox.Show(ex.Message, "NotchHub Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }
        SetupLog.Info($"── NotchHub setup {ctx.Version} started: {string.Join(' ', e.Args)} (exe {Environment.ProcessPath})");

        Loc.Register("NotchHub-Setup");
        // Polish by default, like the app (AppSettings.Language = "pl"); English is one chip away on the options page
        var lang = ctx.Language is "pl" or "en" ? ctx.Language : "pl";
        Loc.SetLanguage(lang);
        Motion.Profile = MotionProfile.Refreshed;

        var window = new SetupWindow(ctx);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SelfDelete.RunPending();
        SetupLog.Info($"Setup exiting ({e.ApplicationExitCode})");
        base.OnExit(e);
    }
}
