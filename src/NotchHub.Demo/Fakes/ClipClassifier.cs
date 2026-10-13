using System.Text.RegularExpressions;
using NotchHub.Core.Contracts;

namespace NotchHub.Demo.Fakes;

/// <summary>Guesses the <see cref="ClipKind"/> of text added to the demo history (link, color, code or plain text).</summary>
internal static partial class ClipClassifier
{
    private static readonly string[] CommandPrefixes = ["npm ", "npx ", "git ", "dotnet ", "winget ", "pwsh ", "cd ", "docker ", "pip ", "python "];

    public static ClipKind Classify(string text)
    {
        var trimmed = text.Trim();
        if (ColorPattern().IsMatch(trimmed)) return ClipKind.Color;
        if (!trimmed.Any(char.IsWhiteSpace)
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "ftp")
            return ClipKind.Link;
        if (CommandPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return ClipKind.Code;
        if (trimmed.Contains('\n') && (trimmed.Contains('{') || trimmed.Contains(';') || trimmed.Contains("=>"))) return ClipKind.Code;
        return ClipKind.Text;
    }

    [GeneratedRegex(@"^(#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*(,\s*[\d.]+\s*)?\))$")]
    private static partial Regex ColorPattern();
}
