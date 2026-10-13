using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NotchHub.Core;

namespace NotchHub.Services.Calendar;

/// <summary>The persisted Google grant: refresh token + the client it belongs to + the account e-mail.</summary>
internal sealed record GoogleStoredToken(string ClientId, string RefreshToken, string? Email);

/// <summary>Stores the Google grant encrypted with DPAPI (CurrentUser) in google-token.bin. Never logs the token.</summary>
internal sealed class GoogleTokenStore(string path)
{
    private static readonly byte[] Entropy = "NotchHub.GoogleCalendar.v1"u8.ToArray();

    public string Path { get; } = path;

    public GoogleStoredToken? Load()
    {
        try
        {
            if (!File.Exists(Path) || !OperatingSystem.IsWindows()) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(Path), Entropy, DataProtectionScope.CurrentUser);
            var token = JsonSerializer.Deserialize<GoogleStoredToken>(Encoding.UTF8.GetString(plain));
            return token is { ClientId.Length: > 0, RefreshToken.Length: > 0 } ? token : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            Log.Warn($"CalendarService: stored Google token unreadable ({ex.GetType().Name})");
            return null;
        }
    }

    public void Save(GoogleStoredToken token)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token));
            var data = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Log.Warn($"CalendarService: failed to store Google token ({ex.GetType().Name})");
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"CalendarService: failed to delete Google token ({ex.GetType().Name})");
        }
    }
}
