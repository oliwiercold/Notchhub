using System.Reflection;

namespace NotchHub.Core;

/// <summary>
/// OAuth clients shipped with NotchHub builds so users can sign in with one click (no own Google Cloud / Spotify app needed).
/// The values are NOT stored in the source code: they are injected at build time as assembly metadata from MSBuild
/// properties <c>NotchHubGoogleClientId</c> / <c>NotchHubGoogleClientSecret</c> — taken from the git-ignored
/// <c>secrets.props</c> in the repository root (see <c>secrets.example.props</c>) or from environment variables
/// (GitHub Actions repository secrets in the release workflow). Builds without them simply have no built-in client;
/// users can still enter their own client in Settings → advanced.
/// </summary>
public static class BuiltInClients
{
    private static readonly Dictionary<string, string> Meta = typeof(BuiltInClients).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Where(a => a.Key.StartsWith("NotchHub.", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(a.Value))
        .ToDictionary(a => a.Key, a => a.Value!.Trim());

    /// <summary>Google OAuth client of type "Desktop app" (project "NotchHub", consent screen in production).</summary>
    public static string GoogleClientId => Meta.GetValueOrDefault("NotchHub.GoogleClientId", "");
    public static string GoogleClientSecret => Meta.GetValueOrDefault("NotchHub.GoogleClientSecret", "");

    public static bool HasGoogle => GoogleClientId.Length > 0 && GoogleClientSecret.Length > 0;
}
