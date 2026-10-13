using Microsoft.Win32;

namespace NotchHub.Installer.Engine;

/// <summary>HKCU uninstall entry and the Run (autostart) value. Every write is skipped (and logged) in dry-run mode.</summary>
public static class RegistryOps
{
    // same value name / format / StartupApproved handling as NotchHub.Platform.Shell.Autostart.AutostartService
    public const string RunValueName = "NotchHub";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static void WriteUninstallEntry(SetupContext ctx, string dir)
    {
        var exe = Path.Combine(dir, SetupContext.AppExe);
        var values = new (string Name, object Value, RegistryValueKind Kind)[]
        {
            ("DisplayName", "NotchHub", RegistryValueKind.String),
            ("DisplayVersion", ctx.Version, RegistryValueKind.String),
            ("Publisher", "gimpostin", RegistryValueKind.String),
            ("DisplayIcon", $"\"{exe}\",0", RegistryValueKind.String),
            ("UninstallString", $"\"{Path.Combine(dir, SetupContext.UninstallExe)}\" --uninstall", RegistryValueKind.String),
            ("InstallLocation", dir, RegistryValueKind.String),
            ("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String),
            ("EstimatedSize", (int)Math.Min(int.MaxValue, FileOps.DirectorySize(dir) / 1024), RegistryValueKind.DWord),
            ("NoModify", 1, RegistryValueKind.DWord),
            ("NoRepair", 1, RegistryValueKind.DWord),
        };
        if (ctx.DryRun)
        {
            foreach (var v in values) SetupLog.Info($@"[dry-run] would set HKCU\{SetupContext.UninstallKeyPath}\{v.Name} = {v.Value}");
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(SetupContext.UninstallKeyPath, writable: true);
        foreach (var v in values) key.SetValue(v.Name, v.Value, v.Kind);
        SetupLog.Info("Uninstall entry written");
    }

    public static void DeleteUninstallEntry(SetupContext ctx)
    {
        if (ctx.DryRun)
        {
            SetupLog.Info($@"[dry-run] would delete HKCU\{SetupContext.UninstallKeyPath}");
            return;
        }
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(SetupContext.UninstallKeyPath, throwOnMissingSubKey: false);
            SetupLog.Info("Uninstall entry removed");
        }
        catch (Exception ex)
        {
            SetupLog.Warn("Removing the uninstall entry failed", ex);
        }
    }

    public static string? ReadRunCommand()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            return run?.GetValue(RunValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Creates/refreshes the Run value for <paramref name="exe"/>. <paramref name="enable"/> also marks it enabled in Startup apps.</summary>
    public static void SetAutostart(SetupContext ctx, string exe, bool enable)
    {
        var command = $"\"{exe}\" --autostart";
        if (ctx.DryRun)
        {
            SetupLog.Info($@"[dry-run] would set HKCU\{RunKey}\{RunValueName} = {command}" + (enable ? " (+ StartupApproved enabled)" : ""));
            return;
        }
        using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        run.SetValue(RunValueName, command, RegistryValueKind.String);
        if (enable)
        {
            using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey, writable: true);
            approved.SetValue(RunValueName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        SetupLog.Info("Autostart value written");
    }

    public static void RemoveAutostart(SetupContext ctx)
    {
        if (ctx.DryRun)
        {
            SetupLog.Info($@"[dry-run] would delete HKCU\{RunKey}\{RunValueName} (+ StartupApproved)");
            return;
        }
        try
        {
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                run?.DeleteValue(RunValueName, throwOnMissingValue: false);
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                approved?.DeleteValue(RunValueName, throwOnMissingValue: false);
            SetupLog.Info("Autostart value removed");
        }
        catch (Exception ex)
        {
            SetupLog.Warn("Removing the autostart value failed", ex);
        }
    }
}
