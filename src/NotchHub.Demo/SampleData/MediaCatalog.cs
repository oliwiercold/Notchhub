using NotchHub.Demo.Imaging;

namespace NotchHub.Demo.SampleData;

/// <summary>One playable item of a demo media session.</summary>
internal sealed record DemoTrack(string Title, string Artist, string Album, TimeSpan Duration, AlbumArt Art);

/// <summary>Static description of a demo media session (one app).</summary>
internal sealed record DemoMediaSource(
    string SessionId,
    string SourceAppName,
    IReadOnlyList<DemoTrack> Tracks,
    bool StartsPlaying,
    TimeSpan StartPosition,
    bool SupportsQueue);

/// <summary>Media sessions shown in demo mode (matches the reference screenshots: lofi radio playing at 0:57 of 62:15).</summary>
internal static class MediaCatalog
{
    public static DemoMediaSource Spotify { get; } = new(
        SessionId: "Spotify.exe",
        SourceAppName: "Spotify",
        Tracks:
        [
            new("lofi beats to study and relax", "lofi radio", "beats to relax/study to", TimeSpan.FromSeconds(62 * 60 + 15), AlbumArt.LofiStudy),
            new("Rainy Window", "Kōhaku Tapes", "Night Shift", TimeSpan.FromSeconds(3 * 60 + 24), AlbumArt.RainyWindow),
            new("Golden Hour Drive", "Sunset Cassette", "Analog Summer", TimeSpan.FromSeconds(4 * 60 + 12), AlbumArt.NightDrive),
        ],
        StartsPlaying: true,
        StartPosition: TimeSpan.FromSeconds(57),
        SupportsQueue: true);

    /// <summary>A paused YouTube video in Chrome (browser sessions expose no queue, shuffle or repeat).</summary>
    public static DemoMediaSource YouTube { get; } = new(
        SessionId: "Chrome",
        SourceAppName: "Google Chrome",
        Tracks:
        [
            new("Material 3 Expressive — first look", "YouTube", "", TimeSpan.FromSeconds(12 * 60 + 47), AlbumArt.VideoThumbnail),
        ],
        StartsPlaying: false,
        StartPosition: TimeSpan.FromSeconds(4 * 60 + 31),
        SupportsQueue: false);
}
