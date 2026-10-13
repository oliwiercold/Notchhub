namespace NotchHub.Core;

/// <summary>
/// OAuth clients shipped with NotchHub so users can sign in with one click (no own Google Cloud / Spotify app needed).
/// Desktop OAuth clients are "public clients": their id (and Google's desktop "secret") are not confidential and may live in
/// open-source code. Empty values = no built-in client; the user can still enter their own in Settings → advanced.
/// </summary>
public static class BuiltInClients
{
    /// <summary>Google OAuth client of type "Desktop app" (project "NotchHub", consent screen in production).</summary>
    public const string GoogleClientId = "";
    public const string GoogleClientSecret = "";

    public static bool HasGoogle => GoogleClientId.Length > 0;
}
