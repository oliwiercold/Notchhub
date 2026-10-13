using Microsoft.Win32;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Shell.Autostart;

/// <summary>
/// Start-with-Windows via <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Also honours (and resets) the
/// Task Manager / Settings "Startup apps" toggle stored under <c>Explorer\StartupApproved\Run</c>.
/// </summary>
public sealed class AutostartService : IAutostartService
{
    private const string ValueName = "NotchHub";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>True when the Run entry exists and is not disabled in the Startup apps list.</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (run?.GetValue(ValueName) is not string command || string.IsNullOrWhiteSpace(command)) return false;
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                // First byte: 0x02 / 0x06 = enabled, 0x03 / 0x07 = disabled by the user.
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
            }
            catch (Exception ex)
            {
                Log.Warn("Autostart: failed to read the Run key", ex);
                return false;
            }
        }
    }

    /// <summary>Adds/removes the Run entry for the current executable.</summary>
    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                run.SetValue(ValueName, $"\"{exe}\" --autostart", RegistryValueKind.String);
                using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey, writable: true);
                approved.SetValue(ValueName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            Log.Info($"Autostart: {(enabled ? "enabled" : "disabled")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Autostart: failed to update the Run key", ex);
            return false;
        }
    }
}
