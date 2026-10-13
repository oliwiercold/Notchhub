using System.IO;
using System.Reflection;

namespace NotchHub.UI.Controls;

/// <summary>Material Symbols name → codepoint map (from the official .codepoints file, embedded).
/// Names are the snake_case names from fonts.google.com/icons, e.g. "wifi", "bluetooth", "volume_up".</summary>
public static class IconMap
{
    private static readonly Lazy<Dictionary<string, string>> Map = new(Load);

    public static bool TryGet(string name, out string text) => Map.Value.TryGetValue(name, out text!);

    /// <summary>Returns the glyph text for <paramref name="name"/>, or the "question_mark" glyph when unknown.</summary>
    public static string Get(string name) =>
        TryGet(name, out var t) ? t : (TryGet("question_mark", out var q) ? q : "?");

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotchHub.UI.MaterialSymbolsRounded.codepoints");
        if (stream is null) return map;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var sp = line.IndexOf(' ');
            if (sp <= 0) continue;
            var cp = Convert.ToInt32(line[(sp + 1)..].Trim(), 16);
            map.TryAdd(line[..sp], char.ConvertFromUtf32(cp));
        }
        return map;
    }
}
