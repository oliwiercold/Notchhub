using System.Security;
using System.Text;

namespace NotchHub.Platform.Connectivity.Network.Wlan;

/// <summary>
/// Builds WLAN profile XML (WLANProfile v1 schema) for personal networks from the auth / cipher reported by
/// <c>WlanGetAvailableNetworkList</c>: WPA3-Personal (SAE), WPA2-Personal (AES/TKIP), WPA-Personal, WEP, OWE and open.
/// Enterprise (802.1X) networks are not supported (null) — the UI sends those to Windows settings.
/// </summary>
internal static class WlanProfileXml
{
    /// <summary>Returns the profile XML, or null when the security type is unsupported or the key is invalid for it.</summary>
    public static string? Build(string ssid, byte[] ssidBytes, uint auth, uint cipher, string? password)
    {
        string authName, encName;
        string? keyType = null;
        switch (auth)
        {
            case WlanNative.AuthOpen when cipher is WlanNative.CipherNone:
                authName = "open"; encName = "none";
                break;
            case WlanNative.AuthOpen or WlanNative.AuthSharedKey when cipher is WlanNative.CipherWep or WlanNative.CipherWep40 or WlanNative.CipherWep104:
                authName = auth == WlanNative.AuthSharedKey ? "shared" : "open"; encName = "WEP"; keyType = "networkKey";
                if (password is null || !IsWepKey(password)) return null;
                break;
            case WlanNative.AuthOwe:
                authName = "OWE"; encName = "AES";
                break;
            case WlanNative.AuthWpa3Sae:
                authName = "WPA3SAE"; encName = "AES"; keyType = "passPhrase";
                if (password is null || password.Length is < 8 or > 63) return null;
                break;
            case WlanNative.AuthRsnaPsk or WlanNative.AuthWpaPsk:
                authName = auth == WlanNative.AuthRsnaPsk ? "WPA2PSK" : "WPAPSK";
                encName = cipher == WlanNative.CipherTkip ? "TKIP" : "AES";
                if (password is null) return null;
                if (password.Length is >= 8 and <= 63) keyType = "passPhrase";
                else if (IsHex(password, 64)) keyType = "networkKey";
                else return null;
                break;
            default:
                return null; // enterprise / unknown
        }

        var name = SecurityElement.Escape(ssid);
        var sb = new StringBuilder(640);
        sb.Append("<?xml version=\"1.0\"?>\n")
          .Append("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">\n")
          .Append("  <name>").Append(name).Append("</name>\n")
          .Append("  <SSIDConfig><SSID><hex>").Append(Convert.ToHexString(ssidBytes)).Append("</hex><name>").Append(name).Append("</name></SSID></SSIDConfig>\n")
          .Append("  <connectionType>ESS</connectionType>\n")
          .Append("  <connectionMode>auto</connectionMode>\n")
          .Append("  <MSM><security>\n")
          .Append("    <authEncryption><authentication>").Append(authName).Append("</authentication><encryption>").Append(encName)
          .Append("</encryption><useOneX>false</useOneX></authEncryption>\n");
        if (keyType is not null)
        {
            sb.Append("    <sharedKey><keyType>").Append(keyType).Append("</keyType><protected>false</protected><keyMaterial>")
              .Append(SecurityElement.Escape(password)).Append("</keyMaterial></sharedKey>\n");
        }
        sb.Append("  </security></MSM>\n</WLANProfile>");
        return sb.ToString();
    }

    /// <summary>True when the network needs a key typed by the user.</summary>
    public static bool NeedsKey(uint auth, uint cipher) =>
        !(auth == WlanNative.AuthOwe || auth == WlanNative.AuthOpen && cipher == WlanNative.CipherNone);

    private static bool IsHex(string s, int length) => s.Length == length && s.All(Uri.IsHexDigit);

    private static bool IsWepKey(string s) => s.Length is 5 or 13 || IsHex(s, 10) || IsHex(s, 26);
}
