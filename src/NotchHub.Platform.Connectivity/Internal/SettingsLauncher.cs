using System.Diagnostics;
using NotchHub.Core;

namespace NotchHub.Platform.Connectivity.Internal;

/// <summary>Opens Windows Settings pages (<c>ms-settings:</c> URIs) without ever throwing.</summary>
internal static class SettingsLauncher
{
    public static void Open(string uri)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {uri}", ex);
        }
    }
}
