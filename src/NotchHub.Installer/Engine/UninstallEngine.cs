using System.Diagnostics;

namespace NotchHub.Installer.Engine;

/// <summary>
/// Uninstall: closes the app, removes autostart, shortcuts, the uninstall entry and the program files (only the files setup
/// put there — a folder is removed only when it ends up empty), optionally the user's data. Uninstall.exe cannot delete itself
/// while running: <see cref="SelfDelete"/> schedules that for after the process exits.
/// </summary>
public sealed class UninstallEngine(SetupContext ctx, string installDir, bool removeData)
{
    private static readonly string[] OwnFiles = [SetupContext.AppExe, Path.Combine("Assets", "app.ico")];

    public void Run(IProgress<SetupProgress> progress)
    {
        SetupLog.Info($"Uninstall from {installDir} (data {ctx.DataDir}, remove data {removeData}, dry-run {ctx.DryRun})");

        progress.Report(new(0.1, "setup.step.closing"));
        AppProcesses.CloseAll(ctx.DryRun ? installDir : null, TimeSpan.FromSeconds(4));

        progress.Report(new(0.3, "setup.step.registry"));
        if (RegistryOps.ReadRunCommand() is { } cmd && (ctx.DryRun || cmd.Contains(SetupContext.AppExe, StringComparison.OrdinalIgnoreCase)))
            RegistryOps.RemoveAutostart(ctx);
        RegistryOps.DeleteUninstallEntry(ctx);

        progress.Report(new(0.45, "setup.step.shortcuts"));
        foreach (var lnk in new[] { ShortcutPaths.Desktop, ShortcutPaths.StartMenu })
        {
            if (ctx.DryRun)
            {
                SetupLog.Info($"[dry-run] would remove shortcut {lnk} (if it points into {installDir})");
                continue;
            }
            if (File.Exists(lnk) && ShortcutPaths.IsOurs(lnk, installDir))
                FileOps.TryRetry($"delete {lnk}", () => FileOps.DeleteFile(lnk), 4);
        }

        progress.Report(new(0.6, "setup.step.removing"));
        foreach (var rel in OwnFiles)
        {
            var path = Path.Combine(installDir, rel);
            if (!FileOps.TryRetry($"delete {path}", () => FileOps.DeleteFile(path)))
                throw new IOException($"Could not delete {path}");
        }
        FileOps.DeleteDirectoryIfEmpty(Path.Combine(installDir, "Assets"));
        foreach (var leftover in SafeEnum(() => Directory.GetDirectories(Path.GetDirectoryName(installDir)!, Path.GetFileName(installDir) + ".old-*")))
            FileOps.TryRetry($"delete {leftover}", () => FileOps.DeleteDirectory(leftover), 3);

        var uninstaller = Path.Combine(installDir, SetupContext.UninstallExe);
        if (File.Exists(uninstaller))
        {
            if (string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                SelfDelete.ScheduleSelf(uninstaller, installDir);
            else FileOps.TryRetry($"delete {uninstaller}", () => FileOps.DeleteFile(uninstaller), 4);
        }
        FileOps.DeleteDirectoryIfEmpty(installDir);

        if (removeData)
        {
            progress.Report(new(0.85, "setup.step.data"));
            if (!FileOps.TryRetry($"delete data {ctx.DataDir}", () => FileOps.DeleteDirectory(ctx.DataDir), 6))
                SetupLog.Warn("Some data files could not be removed");
        }

        progress.Report(new(1.0, "setup.step.done"));
        SetupLog.Info("Uninstall finished");
    }

    private static string[] SafeEnum(Func<string[]> f)
    {
        try { return f(); } catch { return []; }
    }
}

/// <summary>Deletes files / folders after this process exits, via a hidden "cmd /c ping … &amp; del …" helper.</summary>
public static class SelfDelete
{
    private static readonly List<string> Commands = [];

    /// <summary>Removes Uninstall.exe and then the program folder (only if empty: rmdir without /s).</summary>
    public static void ScheduleSelf(string exe, string dir)
    {
        lock (Commands)
        {
            Commands.Add($"del /f /q \"{exe}\"");
            Commands.Add($"rmdir \"{Path.Combine(dir, "Assets")}\"");
            Commands.Add($"rmdir \"{dir}\"");
        }
        SetupLog.Info($"Scheduled self-deletion of {exe}");
    }

    /// <summary>A leftover folder of our own (e.g. "NotchHub.old-1234") that was locked during the install.</summary>
    public static void ScheduleDirectory(string dir)
    {
        lock (Commands) Commands.Add($"rmdir /s /q \"{dir}\"");
        SetupLog.Info($"Scheduled deletion of {dir}");
    }

    /// <summary>Starts the helper (call right before exiting). The ping gives this process ~2 s to terminate.</summary>
    public static void RunPending()
    {
        string script;
        lock (Commands)
        {
            if (Commands.Count == 0) return;
            script = "ping 127.0.0.1 -n 3 > nul & " + string.Join(" & ", Commands.Select(c => c + " > nul 2>&1"));
            Commands.Clear();
        }
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/d /c " + script)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath(),
            })?.Dispose();
            SetupLog.Info("Self-deletion helper started");
        }
        catch (Exception ex)
        {
            SetupLog.Error("Starting the self-deletion helper failed", ex);
        }
    }
}
