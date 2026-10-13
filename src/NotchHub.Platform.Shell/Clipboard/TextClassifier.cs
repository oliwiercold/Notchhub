using System.Text.RegularExpressions;
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Shell.Clipboard;

/// <summary>Classifies copied text as Link, Color, Code or plain Text.</summary>
internal static partial class TextClassifier
{
    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"^(?:rgba?|hsla?|hwb|oklch|oklab|lab|lch)\(\s*[-0-9.%deg,/\s]+\)$", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionColor();

    [GeneratedRegex(@"^0x[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?$")]
    private static partial Regex HexLiteralColor();

    [GeneratedRegex(@"^\s*(?:using|import|from|package|namespace|public|private|protected|internal|static|class|struct|interface|enum|def|fn|func|function|const|let|var|return|if|else|for|foreach|while|switch|case|try|catch|#include|#define|SELECT|INSERT|UPDATE|DELETE|CREATE)\b", RegexOptions.Multiline)]
    private static partial Regex CodeKeyword();

    [GeneratedRegex(@"(?:[;{}]\s*$|=>|->|::|\)\s*\{|</?[a-zA-Z][\w-]*[^>]*>|^\s*//|^\s*#\s|\$\(|\bnull\b|\bnew \w+\()", RegexOptions.Multiline)]
    private static partial Regex CodeSymbol();

    public static ClipKind Classify(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return ClipKind.Text;

        if (t.Length <= 64 && (HexColor().IsMatch(t) || FunctionColor().IsMatch(t) || HexLiteralColor().IsMatch(t))) return ClipKind.Color;

        if (t.Length <= 4096 && !t.Any(char.IsWhiteSpace) && IsLink(t)) return ClipKind.Link;

        if (LooksLikeCode(t)) return ClipKind.Code;
        return ClipKind.Text;
    }

    private static bool IsLink(string t)
    {
        if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && t.IndexOf('.', 4) > 4) return true;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme is "http" or "https" or "ftp" or "mailto" or "file" or "ms-settings" or "steam" or "spotify"
               && (uri.Scheme != "file" || t.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeCode(string t)
    {
        var lines = t.Split('\n');
        var keywordHits = CodeKeyword().Count(t);
        var symbolHits = CodeSymbol().Count(t);
        if (lines.Length == 1)
        {
            // One-liners: need strong evidence (e.g. "var x = Foo();" or "<div class=...>").
            return symbolHits >= 2 || (keywordHits >= 1 && symbolHits >= 1);
        }
        var indented = lines.Count(l => l.StartsWith("    ", StringComparison.Ordinal) || l.StartsWith('\t'));
        var score = keywordHits * 2 + symbolHits + (indented >= 2 ? 2 : 0);
        // Normalise for long prose with occasional punctuation.
        return score >= 4 && score * 4 >= lines.Length;
    }
}
