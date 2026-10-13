namespace NotchHub.Demo.Imaging;

/// <summary>Every generated icon used by the fakes (process list, audio mixer, phone notifications).</summary>
internal static class DemoIcons
{
    private const int ProcessSize = 32; // contract: 32x32 PNG of the executable icon
    private const int MixerSize = 64;
    private const int PhoneSize = 96;

    // ── Process groups (Stats "Top apps") — rounded squares ──
    public static readonly IconSpec Chrome = App("chrome", "G", 0x4285F4);
    public static readonly IconSpec VsCode = App("code", "V", 0x0A7ACA);
    public static readonly IconSpec Spotify = App("spotify", "S", 0x1DB954);
    public static readonly IconSpec Discord = App("discord", "D", 0x5865F2);
    public static readonly IconSpec Steam = App("steam", "S", 0x2A475E);
    public static readonly IconSpec WebView = App("msedgewebview2", "W", 0x0C8AA8);
    public static readonly IconSpec Explorer = App("explorer", "E", 0xE8A93A);
    public static readonly IconSpec Terminal = App("windowsterminal", ">_", 0x3C3F45);
    public static readonly IconSpec NotchHub = App("notchhub", "N", 0xC9A24E);

    // ── Audio mixer sessions — circles ──
    public static readonly IconSpec MixerSpotify = new("mixer:spotify", "S", 0x1DB954, IconShape.Circle, MixerSize);
    public static readonly IconSpec MixerChrome = new("mixer:chrome", "G", 0x4285F4, IconShape.Circle, MixerSize);
    public static readonly IconSpec MixerDiscord = new("mixer:discord", "D", 0x5865F2, IconShape.Circle, MixerSize);

    // ── Phone notification app icons — circles ──
    public static readonly IconSpec PhoneMessages = Phone("messages", "", 0x1A73E8, IconGlyph.ChatBubble);
    public static readonly IconSpec PhoneGmail = Phone("gmail", "", 0xEA4335, IconGlyph.Envelope);
    public static readonly IconSpec PhoneDiscord = Phone("discord", "D", 0x5865F2);
    public static readonly IconSpec PhoneYouTube = Phone("youtube", "", 0xFF0033, IconGlyph.Play);
    public static readonly IconSpec PhoneCalendar = Phone("calendar", "31", 0x1A73E8, IconGlyph.CalendarPage);
    public static readonly IconSpec PhoneWhatsApp = Phone("whatsapp", "", 0x25D366, IconGlyph.ChatBubble);
    public static readonly IconSpec PhoneAllegro = Phone("allegro", "", 0xFF5A00, IconGlyph.Bag);
    public static readonly IconSpec PhoneInstagram = Phone("instagram", "I", 0xD62976);
    public static readonly IconSpec PhoneSlack = Phone("slack", "#", 0x611F69);
    public static readonly IconSpec PhoneWeather = Phone("weather", "", 0x4A90D9, IconGlyph.Cloud);

    /// <summary>All icons, used to pre-generate them in the background.</summary>
    public static IReadOnlyList<IconSpec> All { get; } =
    [
        Chrome, VsCode, Spotify, Discord, Steam, WebView, Explorer, Terminal, NotchHub,
        MixerSpotify, MixerChrome, MixerDiscord,
        PhoneMessages, PhoneGmail, PhoneDiscord, PhoneYouTube, PhoneCalendar,
        PhoneWhatsApp, PhoneAllegro, PhoneInstagram, PhoneSlack, PhoneWeather,
    ];

    private static IconSpec App(string key, string text, uint color) => new($"app:{key}", text, color, IconShape.Squircle, ProcessSize);

    private static IconSpec Phone(string key, string text, uint color, IconGlyph glyph = IconGlyph.Text) =>
        new($"phone:{key}", text, color, IconShape.Circle, PhoneSize, glyph);
}
