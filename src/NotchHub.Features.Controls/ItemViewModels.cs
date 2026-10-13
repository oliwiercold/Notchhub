using CommunityToolkit.Mvvm.ComponentModel;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Features.Controls;

public sealed partial class WifiItemViewModel : ObservableObject
{
    public required WifiNetwork Network { get; init; }
    public string Ssid => Network.Ssid;
    public bool IsConnected => Network.IsConnected;
    public bool IsSecured => Network.IsSecured;
    public string Icon => ControlsViewModel.WifiIcon(Network.SignalPercent switch { >= 75 => 4, >= 50 => 3, >= 25 => 2, > 0 => 1, _ => 0 });
    [ObservableProperty] private string _detail = "";

    /// <summary>Connect request in flight (row spinner).</summary>
    [ObservableProperty] private bool _isBusy;
    /// <summary>Inline password field shown (secured network without a working saved profile).</summary>
    [ObservableProperty] private bool _isPasswordOpen;
    /// <summary>Last attempt failed (wrong password / could not connect).</summary>
    [ObservableProperty] private bool _failed;
    /// <summary>Connected by us a moment ago (✓ before the next scan refreshes the list).</summary>
    [ObservableProperty] private bool _justConnected;

    public bool ShowCheck => IsConnected || JustConnected;
    public bool ShowLock => IsSecured && !ShowCheck && !IsBusy;

    partial void OnJustConnectedChanged(bool value) { OnPropertyChanged(nameof(ShowCheck)); OnPropertyChanged(nameof(ShowLock)); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowLock));
}

public sealed partial class BtDeviceViewModel : ObservableObject
{
    public required BluetoothDeviceInfo Device { get; init; }
    public string Id => Device.Id;
    public string Name => Device.Name;
    public bool IsConnected => Device.IsConnected;
    public string Icon => Device.Kind switch
    {
        BluetoothDeviceKind.Audio => "headphones",
        BluetoothDeviceKind.Input => "keyboard",
        BluetoothDeviceKind.Phone => "smartphone",
        BluetoothDeviceKind.Computer => "computer",
        BluetoothDeviceKind.Wearable => "watch",
        _ => "devices_other",
    };
    public string? BatteryText => Device.BatteryPercent is int b ? $"{b}%" : null;
    public required string StatusText { get; init; }
    public required string ActionText { get; init; }

    [ObservableProperty] private bool _isBusy;
}

public sealed partial class OutputViewModel : ObservableObject
{
    public required AudioDevice Device { get; init; }
    public string Id => Device.Id;
    public string Name => Device.Name;
    public string? Description => Device.Description;
    public bool IsDefault => Device.IsDefault;
    public string Icon => ControlsViewModel.OutputIcon(Device.Kind);
    public string RadioIcon => Device.IsDefault ? "radio_button_checked" : "radio_button_unchecked";
}

/// <summary>Per-app mixer row. Local edits win over service echoes for a short time ("never fight the user").</summary>
public sealed partial class AppSessionViewModel : ObservableObject
{
    private bool _syncing;
    private long _lastLocalEdit;

    public AppSessionViewModel(AudioAppSession s)
    {
        Id = s.Id;
        Update(s);
    }

    public string Id { get; }
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private byte[]? _iconPng;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _isMuted;

    public string MuteIcon => IsMuted || Volume <= 0 ? "volume_off" : "volume_up";

    public void Update(AudioAppSession s)
    {
        _syncing = true;
        try
        {
            DisplayName = s.DisplayName;
            if (!ReferenceEquals(IconPng, s.IconPng) && (IconPng is null || s.IconPng is null || !IconPng.AsSpan().SequenceEqual(s.IconPng)))
                IconPng = s.IconPng;
            if (Environment.TickCount64 - _lastLocalEdit > 600)
            {
                Volume = Math.Round(s.Volume * 100);
                IsMuted = s.IsMuted;
            }
        }
        finally { _syncing = false; }
    }

    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(MuteIcon));
        if (_syncing) return;
        _lastLocalEdit = Environment.TickCount64;
        AppServices.Audio.SetSessionVolume(Id, (float)(value / 100.0));
        if (IsMuted && value > 0) { IsMuted = false; }
    }

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(MuteIcon));
        if (_syncing) return;
        _lastLocalEdit = Environment.TickCount64;
        AppServices.Audio.SetSessionMuted(Id, value);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;
}

public sealed partial class MediaChipViewModel(ControlsViewModel owner, string sessionId, string name, bool selected) : ObservableObject
{
    public string SessionId { get; } = sessionId;
    public string Name { get; } = name;
    private bool _isSelected = selected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected) return;
            if (!value)
            {
                // A chip cannot be "unselected" by clicking it again — re-assert the check.
                AppServices.Ui.Post(() => OnPropertyChanged(nameof(IsSelected)));
                return;
            }
            _isSelected = true;
            OnPropertyChanged();
            owner.SelectMediaSession(SessionId);
        }
    }
}
