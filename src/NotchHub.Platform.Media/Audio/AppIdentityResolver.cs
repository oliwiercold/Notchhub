using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NotchHub.Core;
using NotchHub.Platform.Media.Common;
using Windows.ApplicationModel;
using Windows.Storage.Streams;

namespace NotchHub.Platform.Media.Audio;

/// <summary>Who owns an audio session: resolved once per session when it is first seen.</summary>
internal sealed record AppIdentity(string Key, string DisplayName, string? ExePath, string? PackageAumid, string? IconLocation);

/// <summary>
/// Display names and 32 px PNG icons of the processes behind audio sessions. Names are resolved synchronously
/// (cheap, cached); icons are produced on the thread pool and cached per executable / package / icon location.
/// </summary>
internal sealed class AppIdentityResolver
{
    public const int IconSize = 32;
    private const string SystemSoundsNameResource = @"@%SystemRoot%\System32\AudioSrv.Dll,-202";
    private const string SystemSoundsIconResource = @"@%SystemRoot%\System32\AudioSrv.Dll,-203";
    private const string SystemSoundsFallbackName = "System Sounds";

    private readonly ConcurrentDictionary<string, Task<byte[]?>> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public AppIdentity ResolveSystemSounds(string? sessionDisplayName, string? sessionIconPath)
    {
        var name = AppNames.ResolveIndirect(sessionDisplayName)
            ?? _names.GetOrAdd("system", static _ => AppNames.ResolveIndirect(SystemSoundsNameResource) ?? SystemSoundsFallbackName);
        var icon = string.IsNullOrWhiteSpace(sessionIconPath) ? SystemSoundsIconResource : sessionIconPath;
        return new AppIdentity("system", name, null, null, icon);
    }

    public AppIdentity Resolve(uint processId, string? sessionDisplayName, string? sessionIconPath)
    {
        var exePath = ProcessImage.TryGetPath(processId);
        var aumid = ProcessImage.TryGetAppUserModelId(processId);
        var key = aumid ?? exePath ?? $"pid:{processId}";

        // An explicit session name wins (some apps set one); GUID-like or unresolved names are ignored.
        var name = AppNames.ResolveIndirect(sessionDisplayName);
        if (name is not null && (name.StartsWith('@') || Guid.TryParse(name, out _))) name = null;
        name ??= _names.GetOrAdd(key, _ => ResolveProcessName(processId, exePath, aumid));

        var iconLocation = string.IsNullOrWhiteSpace(sessionIconPath) ? null : sessionIconPath;
        return new AppIdentity(key, name, exePath, aumid, iconLocation);
    }

    /// <summary>
    /// Icon of <paramref name="identity"/> if already produced; otherwise starts producing it and invokes
    /// <paramref name="onReady"/> (thread pool) once it is available.
    /// </summary>
    public byte[]? GetIcon(AppIdentity identity, Action onReady)
    {
        var cacheKey = identity.IconLocation ?? identity.PackageAumid ?? identity.ExePath;
        if (cacheKey is null) return null;

        var task = _icons.GetOrAdd(cacheKey, _ => Task.Run(() => ProduceIconAsync(identity)));
        if (task.IsCompletedSuccessfully) return task.Result;
        if (!task.IsCompleted)
        {
            task.ContinueWith(static (_, state) => Log.Safe("Audio: icon ready callback", (Action)state!), onReady,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return null;
    }

    private static string ResolveProcessName(uint processId, string? exePath, string? aumid)
    {
        if (aumid is not null && AppNames.PackagedDisplayName(aumid) is { } packaged) return packaged;
        if (exePath is not null)
            return AppNames.FileDescription(exePath) ?? Path.GetFileNameWithoutExtension(exePath);
        return $"PID {processId}";
    }

    private static async Task<byte[]?> ProduceIconAsync(AppIdentity identity)
    {
        try
        {
            if (identity.IconLocation is not null && IconPng.FromIconLocation(identity.IconLocation, IconSize) is { } fromLocation)
                return fromLocation;
            if (identity.PackageAumid is not null && await TryPackageLogoAsync(identity.PackageAumid).ConfigureAwait(false) is { } logo)
                return logo;
            return identity.ExePath is null ? null : IconPng.FromFile(identity.ExePath, 0, IconSize);
        }
        catch (Exception ex)
        {
            Log.Debug($"Audio: icon for {identity.Key} failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<byte[]?> TryPackageLogoAsync(string aumid)
    {
        const long maxLogoBytes = 512 * 1024;
        try
        {
            var info = AppInfo.GetFromAppUserModelId(aumid);
            var reference = info?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(IconSize, IconSize));
            if (reference is null) return null;

            using var stream = await reference.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (stream.Size == 0 || stream.Size > maxLogoBytes) return null;
            using var reader = new DataReader(stream);
            var loaded = await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(false);
            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            return null;
        }
    }
}
