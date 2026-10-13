using System.Text.RegularExpressions;

namespace NotchHub.Core;

/// <summary>Finds one-time codes (SMS / authenticator / bank confirmations) in notification text, e.g. "Twój kod: 482913",
/// "G-482913 is your Google verification code", "Kod BLIK 123 456".</summary>
public static partial class OtpDetector
{
    /// <summary>Returns the code (digits only, separators removed) or null when the text does not look like it carries a code.</summary>
    public static string? Find(params string?[] texts)
    {
        var text = string.Join("\n", texts.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (text.Length == 0) return null;

        // strongest signal: a keyword followed (or preceded) by the code
        var m = KeywordFirst().Match(text);
        if (!m.Success) m = CodeFirst().Match(text);
        if (m.Success) return Normalize(m.Groups["code"].Value);

        // weaker: a lone 4–8 digit group when the text mentions a code-ish word anywhere
        if (!Hint().IsMatch(text)) return null;
        var lone = Lone().Matches(text).Select(x => x.Groups["code"].Value)
            .Where(c => !LooksLikeYearOrTime(c)).ToList();
        return lone.Count == 1 ? Normalize(lone[0]) : null;
    }

    private static string Normalize(string code) => new(code.Where(char.IsLetterOrDigit).ToArray());

    private static bool LooksLikeYearOrTime(string c) =>
        c.Length == 4 && int.TryParse(c, out var y) && y is >= 1990 and <= 2099;

    private const string Keywords =
        "kod|kodem|kodu|hasło jednorazowe|haslo jednorazowe|blik|pin|code|otp|passcode|verification|weryfikac\\w*|potwierdzenia|autoryzac\\w*|token";

    [GeneratedRegex(@"(?:" + Keywords + @")\s*(?:to|is|:|-|=|#)?\s*(?:[A-Z]-)?(?<code>\d{3}[ \-]?\d{3}|\d{4,8})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeywordFirst();

    [GeneratedRegex(@"\b(?:[A-Z]-)?(?<code>\d{3}[ \-]?\d{3}|\d{4,8})\s+(?:is|jest|to)\s+(?:your|twój|twoj|twoim|the)?\s*(?:\w+\s+)?(?:" + Keywords + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodeFirst();

    [GeneratedRegex(Keywords, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Hint();

    [GeneratedRegex(@"(?<!\d|\d[.,:/])(?<code>\d{4,8})(?!\d|[.,:/]\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Lone();
}
