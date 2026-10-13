using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.Imaging;

namespace NotchHub.Demo.Fakes;

/// <summary>Demo foreground tracker: never fullscreen, the previous app is always "Visual Studio Code". No OS calls.</summary>
public sealed class FakeForegroundService : FakeBackgroundService, IForegroundService
{
    /// <inheritdoc />
    public bool IsFullscreenAppActive => false;

    /// <inheritdoc />
    public event EventHandler? FullscreenChanged { add { } remove { } }

    /// <inheritdoc />
    public string? LastExternalAppName => "Visual Studio Code";

    /// <inheritdoc />
    public void SetOwnWindow(IntPtr hwnd) { }

    /// <inheritdoc />
    public bool RestorePreviousForeground()
    {
        Log.Info("DEMO: would re-activate Visual Studio Code");
        return true;
    }

    /// <inheritdoc />
    public void SendPasteKeystroke() => Log.Info("DEMO: would send Ctrl+V");
}

/// <summary>Demo hotkeys: every valid gesture "registers" (kept in memory, never fires).</summary>
public sealed class FakeHotkeyService : FakeBackgroundService, IHotkeyService
{
    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase) { "Ctrl", "Control", "Alt", "Shift", "Win" };
    private readonly Dictionary<string, string> _registered = [];

    /// <inheritdoc />
    public bool Register(string id, string gesture, Action callback)
    {
        if (!IsValidGesture(gesture)) return false;
        lock (Gate) _registered[id] = gesture;
        return true;
    }

    /// <inheritdoc />
    public void Unregister(string id)
    {
        lock (Gate) _registered.Remove(id);
    }

    /// <inheritdoc />
    public bool IsValidGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[..^1].All(Modifiers.Contains) && !Modifiers.Contains(parts[^1]);
    }
}

/// <summary>Demo wallpaper: a generated golden-hour landscape (PNG under AppPaths.Dir("demo")).</summary>
public sealed class FakeWallpaperService : FakeBackgroundService, IWallpaperService
{
    private volatile string? _path;

    /// <inheritdoc />
    public string? CurrentPath => _path;

    /// <inheritdoc />
    public uint? SolidColor => null;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        var path = await DemoAssets.WallpaperAsync().ConfigureAwait(false);
        if (path is null || path == _path) return;
        _path = path;
        Raise(Changed);
    }

    /// <inheritdoc />
    public Task<WallpaperPixels?> GetPixelsAsync(int maxSize = 128, CancellationToken ct = default) =>
        DemoAssets.WallpaperPixelsAsync(maxSize).WaitAsync(ct);
}

/// <summary>Demo autostart: an in-memory flag (registry untouched).</summary>
public sealed class FakeAutostartService : IAutostartService
{
    private volatile bool _enabled = true;

    /// <inheritdoc />
    public bool IsEnabled => _enabled;

    /// <inheritdoc />
    public bool SetEnabled(bool enabled)
    {
        _enabled = enabled;
        return true;
    }
}
