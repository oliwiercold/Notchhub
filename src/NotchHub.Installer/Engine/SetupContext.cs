using System.Reflection;
using Microsoft.Win32;

namespace NotchHub.Installer.Engine;

/// <summary>Command line + environment of this setup run.</summary>
/// <remarks>
/// Command line:
///   (none)                 install / upgrade (per-user, %LOCALAPPDATA%\Programs\NotchHub)
///   --uninstall            uninstall mode (Uninstall.exe is started with it from Apps &amp; features)
///   --dry-run              test mode: requires --target and --data; NO registry, shortcut or autostart changes, never
///                          launches the app and only closes NotchHub instances started from the target folder
///   --target &lt;dir&gt;         install folder override
///   --data &lt;dir&gt;           data folder override (default %APPDATA%\NotchHub)
///   --lang pl|en           initial installer language
///   --page options|options-end|done   (testing) open a page directly; "done" runs the install / uninstall at once
///   --auto-close &lt;sec&gt;     (testing) exit after the given number of seconds
///   --remove-data          (uninstall) preselect "also remove my data"
/// </remarks>
public sealed class SetupContext
{
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NotchHub";
    public const string AppExe = "NotchHub.exe";
    public const string UninstallExe = "Uninstall.exe";

    public bool Uninstall { get; private init; }
    public bool DryRun { get; private init; }
    public string? TargetOverride { get; private init; }
    public string DataDir { get; private init; } = "";
    public string? Language { get; private init; }
    public string? StartPage { get; private init; }
    public double AutoCloseSeconds { get; private init; }
    public bool RemoveDataPreset { get; private init; }

    /// <summary>Version of the embedded app (= this setup's assembly version, both come from Directory.Build.props).</summary>
    public string Version { get; } = VersionOf(Assembly.GetExecutingAssembly());

    /// <summary>Install folder of an existing installation (registry InstallLocation), when there is one.</summary>
    public string? ExistingDir { get; private set; }
    public string? ExistingVersion { get; private set; }

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NotchHub");

    public string SettingsPath => Path.Combine(DataDir, "settings.json");
    public bool SettingsExist => File.Exists(SettingsPath);

    public static SetupContext Parse(string[] args)
    {
        string? Arg(string name)
        {
            var i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        var dry = Has("--dry-run");
        var target = Arg("--target");
        var data = Arg("--data");
        if (dry && (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(data)))
            throw new ArgumentException("--dry-run requires --target <dir> and --data <dir>");
        var ctx = new SetupContext
        {
            Uninstall = Has("--uninstall"),
            RemoveDataPreset = Has("--remove-data"),
            DryRun = dry,
            TargetOverride = string.IsNullOrWhiteSpace(target) ? null : Path.GetFullPath(target),
            DataDir = string.IsNullOrWhiteSpace(data)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotchHub")
                : Path.GetFullPath(data),
            Language = Arg("--lang"),
            StartPage = Arg("--page")?.ToLowerInvariant(),
            AutoCloseSeconds = double.TryParse(Arg("--auto-close"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0,
        };
        ctx.DetectExisting();
        return ctx;
    }

    private void DetectExisting()
    {
        if (DryRun)
        {
            // dry-run: the "registry" is the target folder itself
            var exe = Path.Combine(TargetOverride!, AppExe);
            if (File.Exists(exe))
            {
                ExistingDir = TargetOverride;
                ExistingVersion = FileVersion(exe);
            }
            return;
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            var dir = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, AppExe)))
            {
                ExistingDir = Path.TrimEndingDirectorySeparator(dir);
                ExistingVersion = key!.GetValue("DisplayVersion") as string ?? FileVersion(Path.Combine(dir, AppExe));
            }
        }
        catch (Exception ex)
        {
            SetupLog.Warn("Reading the existing installation failed", ex);
        }
        if (ExistingDir is null && File.Exists(Path.Combine(DefaultInstallDir, AppExe)))
        {
            ExistingDir = DefaultInstallDir;
            ExistingVersion = FileVersion(Path.Combine(DefaultInstallDir, AppExe));
        }
    }

    /// <summary>Folder the uninstaller works on: --target, else the folder Uninstall.exe runs from, else the registered one.</summary>
    public string? UninstallDir()
    {
        if (TargetOverride is not null) return TargetOverride;
        var self = Path.GetDirectoryName(Environment.ProcessPath);
        if (self is not null && File.Exists(Path.Combine(self, AppExe))) return self;
        return ExistingDir;
    }

    private static string? FileVersion(string path)
    {
        try
        {
            var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return v.FileMajorPart + "." + v.FileMinorPart + "." + v.FileBuildPart;
        }
        catch
        {
            return null;
        }
    }

    private static string VersionOf(Assembly asm)
    {
        var v = asm.GetName().Version ?? new Version(1, 0, 0);
        return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }
}
