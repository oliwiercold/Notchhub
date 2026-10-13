using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Settings;

/// <summary>General section — Spotify card (own client id, PKCE sign-in, status, how-to).</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>Must equal SpotifyAuth.RedirectUri in NotchHub.Platform.Media (fixed loopback port; Spotify requires an exact match).</summary>
    public const string SpotifyRedirect = "http://127.0.0.1:43819/callback";

    public string SpotifyRedirectUri => SpotifyRedirect;
    [ObservableProperty] private string _spotifyClientId = "";
    [ObservableProperty] private string _spotifyStatusText = "";
    [ObservableProperty] private string? _spotifyErrorText;
    [ObservableProperty] private bool _isSpotifyConnected;
    [ObservableProperty] private bool _isSpotifyBusy;
    [ObservableProperty] private bool _canConnectSpotify;
    [ObservableProperty] private bool _canEditSpotifyClient = true;
    [ObservableProperty] private bool _spotifyHelpOpen;

    private void LoadSpotify()
    {
        SpotifyClientId = S.Media?.SpotifyClientId ?? "";
        LoadSpotifyStatus();
    }

    private void OnMediaChanged(object? sender, EventArgs e) => AppServices.Ui.Post(LoadSpotifyStatus);

    /// <summary>Re-reads the live Spotify state of the media service (UI thread).</summary>
    private void LoadSpotifyStatus()
    {
        var m = AppServices.Media;
        var state = m?.SpotifyState ?? SpotifyConnectionState.NotConfigured;
        // the service sees a typed id only after the deferred save
        if (state == SpotifyConnectionState.NotConfigured && !string.IsNullOrWhiteSpace(SpotifyClientId)) state = SpotifyConnectionState.Disconnected;
        IsSpotifyConnected = state == SpotifyConnectionState.Connected;
        IsSpotifyBusy = state == SpotifyConnectionState.Connecting;
        CanConnectSpotify = m is not null && state is SpotifyConnectionState.Disconnected or SpotifyConnectionState.Error;
        CanEditSpotifyClient = state is not (SpotifyConnectionState.Connected or SpotifyConnectionState.Connecting);
        SpotifyStatusText = state switch
        {
            SpotifyConnectionState.Connected => Loc.Format("settings.Spotify.ConnectedAs", m?.SpotifyAccount ?? "Spotify"),
            SpotifyConnectionState.Connecting => Loc.Get("settings.Spotify.Connecting"),
            SpotifyConnectionState.Disconnected => Loc.Get("settings.Spotify.Disconnected"),
            SpotifyConnectionState.Error => Loc.Get("settings.Spotify.Failed"),
            _ => Loc.Get("settings.Spotify.NotConfigured"),
        };
        SpotifyErrorText = m?.SpotifyError is { } err && state != SpotifyConnectionState.Connecting ? SpotifyErrorMessage(err) : null;
    }

    private static string SpotifyErrorMessage(string code) => code switch
    {
        "denied" => Loc.Get("settings.Spotify.Err.Denied"),
        "session_expired" or "unauthorized" or "not_connected" => Loc.Get("settings.Spotify.Err.Expired"),
        "invalid_client" or "auth_invalid_client" => Loc.Get("settings.Spotify.Err.Client"),
        "timeout" => Loc.Format("settings.Spotify.Err.Timeout", SpotifyRedirect),
        "cancelled" => Loc.Get("settings.Spotify.Err.Cancelled"),
        "port_in_use" => Loc.Get("settings.Spotify.Err.Port"),
        "network" => Loc.Get("settings.Spotify.Err.Network"),
        "premium_required" => Loc.Get("settings.Spotify.Err.Premium"),
        "user_not_allowed" or "forbidden" => Loc.Get("settings.Spotify.Err.User"),
        "rate_limited" => Loc.Get("settings.Spotify.Err.RateLimited"),
        "state_mismatch" or "no_code" or "no_refresh_token" or "token_failed" or "browser" => Loc.Get("settings.Spotify.Err.Flow"),
        _ => Loc.Format("settings.Spotify.Err.Other", code),
    };

    partial void OnSpotifyClientIdChanged(string value)
    {
        Defer("spotifyid", s => s.Media.SpotifyClientId = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        if (!_loading) LoadSpotifyStatus();
    }

    [RelayCommand]
    private async Task ConnectSpotify()
    {
        Flush();
        if (AppServices.Media is not { } m) return;
        IsSpotifyBusy = true;
        CanConnectSpotify = false;
        try { await m.ConnectSpotifyAsync(); }
        catch (Exception ex) { Log.Warn("Settings: Spotify sign-in failed", ex); }
        LoadSpotifyStatus();
    }

    [RelayCommand]
    private async Task DisconnectSpotify()
    {
        if (AppServices.Media is not { } m) return;
        try { await m.DisconnectSpotifyAsync(); }
        catch (Exception ex) { Log.Warn("Settings: Spotify disconnect failed", ex); }
        LoadSpotifyStatus();
    }

    [RelayCommand] private void ToggleSpotifyHelp() => SpotifyHelpOpen = !SpotifyHelpOpen;
}
