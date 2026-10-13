using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotchHub.Core;

/// <summary>Atomic JSON persistence (write temp file + replace). Thread-safe per path.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    private static object LockFor(string path) => Locks.GetOrAdd(Path.GetFullPath(path), _ => new object());

    /// <summary>Loads JSON from <paramref name="path"/>; returns <paramref name="fallback"/>() when missing or corrupt.</summary>
    public static T Load<T>(string path, Func<T> fallback)
    {
        lock (LockFor(path))
        {
            try
            {
                if (!File.Exists(path)) return fallback();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
            }
            catch (Exception ex)
            {
                Log.Warn($"JsonStore: failed to read {path}, using defaults", ex);
                try { File.Copy(path, path + ".corrupt", overwrite: true); } catch { }
                return fallback();
            }
        }
    }

    public static void Save<T>(string path, T value)
    {
        lock (LockFor(path))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error($"JsonStore: failed to write {path}", ex);
            }
        }
    }
}
