namespace NotchHub.Core;

/// <summary>
/// Locations of all persisted data. Every service stores its files below <see cref="Root"/>.
/// The root can be overridden (demo mode, tests) via <see cref="Initialize"/> or the NOTCHHUB_DATA env variable.
/// </summary>
public static class AppPaths
{
    private static string? _root;

    public static string Root
    {
        get
        {
            if (_root is null)
            {
                var env = Environment.GetEnvironmentVariable("NOTCHHUB_DATA");
                _root = string.IsNullOrWhiteSpace(env)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotchHub")
                    : env;
                Directory.CreateDirectory(_root);
            }
            return _root;
        }
    }

    /// <summary>Overrides the data root. Call once at startup before any service is created.</summary>
    public static void Initialize(string? overrideRoot)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            _root = Path.GetFullPath(overrideRoot);
            Directory.CreateDirectory(_root);
        }
    }

    /// <summary>Path of a file directly under <see cref="Root"/> (e.g. "settings.json").</summary>
    public static string File(string fileName) => Path.Combine(Root, fileName);

    /// <summary>Path of a sub directory of <see cref="Root"/>; created if missing.</summary>
    public static string Dir(string name)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Logs => Dir("logs");
}
