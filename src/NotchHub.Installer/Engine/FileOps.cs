namespace NotchHub.Installer.Engine;

/// <summary>File-system helpers with retries (antivirus scanners, Explorer and the indexer briefly lock fresh files).</summary>
public static class FileOps
{
    public static void Retry(string what, Action action, int attempts = 12)
    {
        for (var i = 1; ; i++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (i < attempts && ex is IOException or UnauthorizedAccessException)
            {
                SetupLog.Warn($"{what}: attempt {i} failed, retrying", ex);
                Thread.Sleep(Math.Min(100 * i, 800));
            }
        }
    }

    public static bool TryRetry(string what, Action action, int attempts = 12)
    {
        try
        {
            Retry(what, action, attempts);
            return true;
        }
        catch (Exception ex)
        {
            SetupLog.Warn($"{what}: gave up", ex);
            return false;
        }
    }

    public static void WriteStream(Stream source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Retry($"write {path}", () =>
        {
            source.Position = 0;
            using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            source.CopyTo(target);
        });
    }

    public static void Copy(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Retry($"copy {from} -> {to}", () => File.Copy(from, to, overwrite: true));
    }

    public static void DeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        Retry($"delete {path}", () =>
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        });
    }

    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        Retry($"delete dir {path}", () =>
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        });
    }

    /// <summary>Deletes the directory only when it is empty (never removes anything the user put there).</summary>
    public static void DeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
        catch (Exception ex)
        {
            SetupLog.Warn($"rmdir {path}", ex);
        }
    }

    public static long DirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>True when <paramref name="child"/> is <paramref name="parent"/> or inside it.</summary>
    public static bool IsUnder(string? child, string parent)
    {
        if (string.IsNullOrEmpty(child)) return false;
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child)) + Path.DirectorySeparatorChar;
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}
