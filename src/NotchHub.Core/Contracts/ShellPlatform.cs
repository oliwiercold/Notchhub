namespace NotchHub.Core.Contracts;

// ───────────────────────────── Clipboard ─────────────────────────────

public enum ClipKind { Text, Link, Code, Color, Image, Files }

public enum ClipOrigin { Local, Phone }

public sealed record ClipItem
{
    public required Guid Id { get; init; }
    public required ClipKind Kind { get; init; }
    /// <summary>Text content (Text/Link/Code/Color). For Files: paths joined with newlines.</summary>
    public string? Text { get; init; }
    /// <summary>Full-size PNG on disk (Image).</summary>
    public string? ImagePath { get; init; }
    /// <summary>Small PNG (longest side ≤ 320 px) on disk (Image).</summary>
    public string? ThumbnailPath { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public IReadOnlyList<string>? Files { get; init; }
    public DateTimeOffset CopiedAt { get; init; }
    public bool IsPinned { get; init; }
    /// <summary>Friendly name of the app that was in the foreground when copied.</summary>
    public string? SourceApp { get; init; }
    public ClipOrigin Origin { get; init; }
}

public interface IClipboardService : IBackgroundService
{
    /// <summary>History, newest first (pinned items are included; UIs group them).</summary>
    IReadOnlyList<ClipItem> Items { get; }
    bool IsMonitoring { get; }
    event EventHandler? Changed;
    void SetPinned(Guid id, bool pinned);
    void Delete(Guid id);
    void Delete(IReadOnlyList<Guid> ids);
    void Clear(bool keepPinned = true);
    /// <summary>Puts the item(s) on the system clipboard (several items → texts joined by newlines, files combined).
    /// Moves the item to the top instead of creating a duplicate.</summary>
    Task<bool> CopyToClipboardAsync(IReadOnlyList<Guid> ids);
    /// <summary>Copies, collapses the shell, re-activates the previously focused app and sends Ctrl+V.</summary>
    Task<bool> PasteAsync(IReadOnlyList<Guid> ids);
    /// <summary>Adds text that arrived from elsewhere (phone) and optionally puts it on the system clipboard.</summary>
    void AddExternal(string text, ClipOrigin origin, bool setSystemClipboard);
    /// <summary>Current text on the system clipboard, if any.</summary>
    Task<string?> GetSystemClipboardTextAsync();
}

// ───────────────────────────── Foreground / fullscreen ─────────────────────────────

public interface IForegroundService : IBackgroundService
{
    /// <summary>A fullscreen app (game, video, presentation) is in the foreground on the notch's monitor.</summary>
    bool IsFullscreenAppActive { get; }
    event EventHandler? FullscreenChanged;
    /// <summary>Friendly name of the last foreground app that is not NotchHub.</summary>
    string? LastExternalAppName { get; }
    /// <summary>Tells the tracker which window is ours (ignored for "previous window" tracking; its monitor is checked for fullscreen).</summary>
    void SetOwnWindow(IntPtr hwnd);
    /// <summary>Re-activates the last external foreground window. Returns false if none / failed.</summary>
    bool RestorePreviousForeground();
    /// <summary>Sends Ctrl+V to the foreground window via SendInput.</summary>
    void SendPasteKeystroke();
}

// ───────────────────────────── Hotkeys ─────────────────────────────

public interface IHotkeyService : IBackgroundService
{
    /// <summary>Registers or replaces a global hotkey. <paramref name="gesture"/> like "Ctrl+Alt+Space", "Win+Shift+N".
    /// Callback runs on an arbitrary thread. Returns false if the gesture is invalid or already taken by another app.</summary>
    bool Register(string id, string gesture, Action callback);
    void Unregister(string id);
    /// <summary>True if the gesture string can be parsed.</summary>
    bool IsValidGesture(string gesture);
}

// ───────────────────────────── Wallpaper ─────────────────────────────

/// <summary>Downscaled wallpaper pixels, row-major 0xAARRGGBB.</summary>
public sealed record WallpaperPixels(int Width, int Height, uint[] Argb);

public interface IWallpaperService : IBackgroundService
{
    /// <summary>Image path of the wallpaper on the notch's monitor (null for solid color / unknown).</summary>
    string? CurrentPath { get; }
    /// <summary>ARGB background color when the wallpaper is a solid color.</summary>
    uint? SolidColor { get; }
    /// <summary>Wallpaper path, file content or solid color changed (slideshows included).</summary>
    event EventHandler? Changed;
    /// <summary>Decodes the wallpaper scaled so the longest side ≤ <paramref name="maxSize"/>. Null when unavailable.</summary>
    Task<WallpaperPixels?> GetPixelsAsync(int maxSize = 128, CancellationToken ct = default);
}

// ───────────────────────────── Autostart ─────────────────────────────

public interface IAutostartService
{
    bool IsEnabled { get; }
    /// <summary>Adds/removes NotchHub from HKCU\...\Run. Returns success.</summary>
    bool SetEnabled(bool enabled);
}
