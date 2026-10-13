using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using NotchHub.Core;

namespace NotchHub.Installer.Engine;

/// <summary>One progress report: 0..1 and the string key of the current step.</summary>
public readonly record struct SetupProgress(double Value, string StepKey);

/// <summary>
/// Install / upgrade. Files are staged in a sibling temp folder and swapped in with directory renames (rolled back on failure),
/// so an interrupted install never leaves a half-written program folder. Runs on a worker thread.
/// </summary>
public sealed class InstallEngine(SetupContext ctx, SetupOptions options)
{
    /// <summary>Zip with NotchHub.exe (packed at build time, see NotchHub.Installer.csproj).</summary>
    public const string PayloadResource = "NotchHub.Payload.zip";
    private const string IconResource = "NotchHub.Payload.app.ico";
    private const string ModsPrefix = "NotchHub.Mods/";

    public string InstallDir { get; } = Path.GetFullPath(options.InstallDir).TrimEnd('\\');
    public string AppPath => Path.Combine(InstallDir, SetupContext.AppExe);

    public static bool HasPayload => Assembly.GetExecutingAssembly().GetManifestResourceInfo(PayloadResource) is not null;

    public void Run(IProgress<SetupProgress> progress)
    {
        SetupLog.Info($"Install {ctx.Version} -> {InstallDir} (data {ctx.DataDir}, dry-run {ctx.DryRun}, upgrade {ctx.ExistingDir is not null})");

        progress.Report(new(0.05, "setup.step.closing"));
        AppProcesses.CloseAll(ctx.DryRun ? InstallDir : null, TimeSpan.FromSeconds(4));

        progress.Report(new(0.15, "setup.step.extracting"));
        var parent = Path.GetDirectoryName(InstallDir) ?? throw new InvalidOperationException("Invalid install folder");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".NotchHub.setup-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Stage(staging);

            progress.Report(new(0.55, "setup.step.files"));
            Swap(staging);
        }
        finally
        {
            if (Directory.Exists(staging)) FileOps.TryRetry("remove staging", () => FileOps.DeleteDirectory(staging), 3);
        }

        progress.Report(new(0.70, "setup.step.settings"));
        var fresh = WriteSettings();
        if (fresh && options.ExampleMods) Step("mods", InstallMods);

        progress.Report(new(0.82, "setup.step.shortcuts"));
        Step("shortcuts", Shortcuts);

        progress.Report(new(0.92, "setup.step.registry"));
        RegistryOps.WriteUninstallEntry(ctx, InstallDir);
        Step("autostart", () => Autostart(fresh));

        progress.Report(new(1.0, "setup.step.done"));
        SetupLog.Info("Install finished");
    }

    /// <summary>Starts the installed app (never in dry-run).</summary>
    public void Launch()
    {
        if (ctx.DryRun)
        {
            SetupLog.Info($"[dry-run] would start {AppPath}");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true, WorkingDirectory = InstallDir })?.Dispose();
        }
        catch (Exception ex)
        {
            SetupLog.Error("Launching NotchHub failed", ex);
        }
    }

    // ───────────────────────────── steps ─────────────────────────────

    private void Stage(string staging)
    {
        Directory.CreateDirectory(staging);
        var asm = Assembly.GetExecutingAssembly();
        using (var payload = asm.GetManifestResourceStream(PayloadResource)
                             ?? throw new InvalidOperationException("This setup was built without the NotchHub.exe payload."))
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            var entry = zip.GetEntry(SetupContext.AppExe) ?? throw new InvalidDataException("The payload does not contain NotchHub.exe");
            var exe = Path.Combine(staging, SetupContext.AppExe);
            FileOps.Retry($"extract {exe}", () => entry.ExtractToFile(exe, overwrite: true));
            if (new FileInfo(exe).Length != entry.Length) throw new IOException("Payload size mismatch after extraction");
        }
        using (var icon = asm.GetManifestResourceStream(IconResource))
        {
            if (icon is not null) FileOps.WriteStream(icon, Path.Combine(staging, "Assets", "app.ico"));
        }
        var self = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(self) && File.Exists(self))
            FileOps.Copy(self, Path.Combine(staging, SetupContext.UninstallExe));
        else SetupLog.Warn("Could not locate the setup executable - Uninstall.exe not created");
        SetupLog.Info($"Staged into {staging}");
    }

    private void Swap(string staging)
    {
        if (!Directory.Exists(InstallDir))
        {
            FileOps.Retry("move staging into place", () => Directory.Move(staging, InstallDir));
            SetupLog.Info("Fresh program folder created");
            return;
        }

        var old = InstallDir + ".old-" + Guid.NewGuid().ToString("N")[..8];
        if (FileOps.TryRetry("rename current program folder", () => Directory.Move(InstallDir, old), 8))
        {
            try
            {
                FileOps.Retry("move staging into place", () => Directory.Move(staging, InstallDir));
            }
            catch
            {
                SetupLog.Warn("Swap failed - restoring the previous program folder");
                FileOps.Retry("restore previous folder", () => Directory.Move(old, InstallDir));
                throw;
            }
            // keep anything the user put next to the app (e.g. a portable "mods" folder)
            foreach (var entry in Directory.EnumerateFileSystemEntries(old))
            {
                var dest = Path.Combine(InstallDir, Path.GetFileName(entry));
                if (File.Exists(dest) || Directory.Exists(dest)) continue;
                FileOps.TryRetry($"carry over {entry}", () =>
                {
                    if (Directory.Exists(entry)) Directory.Move(entry, dest);
                    else File.Move(entry, dest);
                }, 3);
            }
            if (!FileOps.TryRetry("remove previous folder", () => FileOps.DeleteDirectory(old), 4))
                SelfDelete.ScheduleDirectory(old);
            SetupLog.Info("Program folder swapped");
            return;
        }

        // something keeps the folder open (Explorer window, a shell with that cwd): replace file by file with rollback
        SetupLog.Warn("Folder rename not possible - replacing files in place");
        var done = new List<(string Target, string? Backup)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(InstallDir, Path.GetRelativePath(staging, file));
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = target + ".setup-old";
                    FileOps.DeleteFile(backup);
                    var b = backup;
                    FileOps.Retry($"backup {target}", () => File.Move(target, b));
                }
                done.Add((target, backup));
                FileOps.Copy(file, target);
            }
        }
        catch
        {
            foreach (var (target, backup) in Enumerable.Reverse(done))
            {
                FileOps.TryRetry($"rollback {target}", () =>
                {
                    FileOps.DeleteFile(target);
                    if (backup is not null) File.Move(backup, target);
                }, 4);
            }
            throw;
        }
        foreach (var (_, backup) in done)
            if (backup is not null) FileOps.TryRetry($"remove {backup}", () => FileOps.DeleteFile(backup), 3);
    }

    /// <summary>Writes settings.json only when it does not exist yet. Returns true for a fresh setup.</summary>
    private bool WriteSettings()
    {
        if (File.Exists(ctx.SettingsPath))
        {
            SetupLog.Info("settings.json exists - keeping the user's settings");
            return false;
        }
        Directory.CreateDirectory(ctx.DataDir);
        JsonStore.Save(ctx.SettingsPath, options.ToSettings());
        if (!File.Exists(ctx.SettingsPath)) throw new IOException($"Could not write {ctx.SettingsPath}");
        SetupLog.Info("settings.json written");
        return true;
    }

    private void InstallMods()
    {
        var asm = Assembly.GetExecutingAssembly();
        var groups = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ModsPrefix, StringComparison.Ordinal))
            .Select(n => (Name: n, Rel: n[ModsPrefix.Length..].Replace('/', '\\')))
            .Where(x => x.Rel.Contains('\\'))
            .GroupBy(x => x.Rel[..x.Rel.IndexOf('\\')], StringComparer.OrdinalIgnoreCase);
        var root = Path.Combine(ctx.DataDir, "mods");
        foreach (var g in groups)
        {
            // compiled ("assembly") mods ship as source in the repo - only no-code widget mods are installed
            if (!g.Any(x => x.Rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) && IsAssemblyMod(asm, g.Select(x => x.Name)))
                continue;
            var dir = Path.Combine(root, g.Key);
            if (Directory.Exists(dir)) continue;
            foreach (var (name, rel) in g)
            {
                using var s = asm.GetManifestResourceStream(name);
                if (s is not null) FileOps.WriteStream(s, Path.Combine(root, rel));
            }
            SetupLog.Info($"Example mod installed: {g.Key}");
        }
    }

    private static bool IsAssemblyMod(Assembly asm, IEnumerable<string> names)
    {
        var manifest = names.FirstOrDefault(n => n.EndsWith("mod.json", StringComparison.OrdinalIgnoreCase)
                                                 && n.Count(c => c is '/' or '\\') == 2);
        if (manifest is null) return true;
        using var s = asm.GetManifestResourceStream(manifest);
        if (s is null) return true;
        using var reader = new StreamReader(s);
        return Regex.IsMatch(reader.ReadToEnd(), "\"kind\"\\s*:\\s*\"assembly\"", RegexOptions.IgnoreCase);
    }

    private void Shortcuts()
    {
        Shortcut(ShortcutPaths.Desktop, options.DesktopShortcut);
        Shortcut(ShortcutPaths.StartMenu, options.StartMenuShortcut);
    }

    private void Shortcut(string lnk, bool wanted)
    {
        if (ctx.DryRun)
        {
            SetupLog.Info($"[dry-run] would {(wanted ? "create" : "remove (if ours)")} shortcut {lnk} -> {AppPath}");
            return;
        }
        if (wanted)
        {
            ShellLink.Create(lnk, AppPath, null, InstallDir, "NotchHub", AppPath);
            SetupLog.Info($"Shortcut created: {lnk}");
        }
        else if (File.Exists(lnk) && ShortcutPaths.IsOurs(lnk, InstallDir))
        {
            FileOps.DeleteFile(lnk);
            SetupLog.Info($"Shortcut removed: {lnk}");
        }
    }

    private void Autostart(bool fresh)
    {
        if (fresh)
        {
            if (options.StartWithWindows) RegistryOps.SetAutostart(ctx, AppPath, enable: true);
            else if (RegistryOps.ReadRunCommand() is { } cmd && cmd.Contains(InstallDir, StringComparison.OrdinalIgnoreCase))
                RegistryOps.RemoveAutostart(ctx);
            return;
        }
        // upgrade / existing settings: keep the user's choice, only refresh the path when the entry exists
        if (RegistryOps.ReadRunCommand() is not null) RegistryOps.SetAutostart(ctx, AppPath, enable: false);
        else SetupLog.Info("Autostart: no Run value - left off");
    }

    private static void Step(string what, Action action)
    {
        // non-critical steps: the program is installed even when e.g. a shortcut cannot be created
        try { action(); }
        catch (Exception ex) { SetupLog.Error($"Step '{what}' failed (continuing)", ex); }
    }
}

public static class ShortcutPaths
{
    public static string Desktop => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NotchHub.lnk");
    public static string StartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "NotchHub.lnk");

    public static bool IsOurs(string lnk, string installDir)
    {
        var target = ShellLink.TargetOf(lnk);
        return target is null || FileOps.IsUnder(target, installDir);
    }
}
