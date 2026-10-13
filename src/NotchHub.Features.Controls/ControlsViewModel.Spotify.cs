using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Controls;

/// <summary>Media card — Spotify extras: "Liked Songs" heart and a hint to connect the account (Web API, see Settings → General).</summary>
public sealed partial class ControlsViewModel
{
    [ObservableProperty] private bool _showLike;
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private bool _showSpotifyHint;
    [ObservableProperty] private string _shuffleTip = "";
    private bool _likeBusy;

    /// <summary>Raised when the heart is toggled by the user (the view plays the "pop").</summary>
    public event EventHandler? LikeToggled;

    private void RefreshSpotify(MediaSessionInfo? cur)
    {
        var m = AppServices.Media;
        bool spotify = cur is not null &&
            (cur.SourceAppId.Contains("spotify", StringComparison.OrdinalIgnoreCase) || cur.SourceAppName.Equals("Spotify", StringComparison.OrdinalIgnoreCase));
        var state = m.SpotifyState;
        ShowLike = spotify && state == SpotifyConnectionState.Connected;
        ShowSpotifyHint = spotify && state != SpotifyConnectionState.Connected && state != SpotifyConnectionState.Connecting;
        if (!_likeBusy) IsLiked = ShowLike && m.IsCurrentTrackLiked == true;
        // Smart Shuffle is not available in Spotify's public Web API — say so instead of faking it.
        ShuffleTip = Loc.Get(spotify ? "controls.shuffleSpotify" : "controls.shuffle");
    }

    [RelayCommand]
    private async Task ToggleLike()
    {
        if (_likeBusy || !ShowLike) return;
        _likeBusy = true;
        var target = !IsLiked;
        IsLiked = target; // optimistic
        LikeToggled?.Invoke(this, EventArgs.Empty);
        try
        {
            if (!await AppServices.Media.SetCurrentTrackLikedAsync(target))
                IsLiked = AppServices.Media.IsCurrentTrackLiked == true;
        }
        catch (Exception ex)
        {
            Log.Warn($"controls: like failed: {ex.Message}");
            IsLiked = !target;
        }
        finally { _likeBusy = false; }
    }

    [RelayCommand]
    private static void OpenSpotifySettings() => AppServices.Shell?.Expand(ShellTab.Settings);
}
