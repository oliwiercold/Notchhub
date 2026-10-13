using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Demo.Fakes;

/// <summary>Demo display settings: night light off, dark mode on, brightness 70 %. Changes stay in memory.</summary>
public sealed class FakeDisplayService : FakeBackgroundService, IDisplayService
{
    private bool _nightLight;
    private bool _darkMode = true;
    private int _brightness = 70;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool NightLightSupported => true;

    /// <inheritdoc />
    public bool NightLightEnabled
    {
        get { lock (Gate) return _nightLight; }
    }

    /// <inheritdoc />
    public bool DarkModeEnabled
    {
        get { lock (Gate) return _darkMode; }
    }

    /// <inheritdoc />
    public bool BrightnessSupported => true;

    /// <inheritdoc />
    public int Brightness
    {
        get { lock (Gate) return _brightness; }
    }

    /// <inheritdoc />
    public Task<bool> SetNightLightAsync(bool enabled) => Set(ref _nightLight, enabled);

    /// <inheritdoc />
    public Task<bool> SetDarkModeAsync(bool enabled) => Set(ref _darkMode, enabled);

    /// <inheritdoc />
    public Task<bool> SetBrightnessAsync(int percent) => Set(ref _brightness, Math.Clamp(percent, 0, 100));

    /// <inheritdoc />
    public void OpenNightLightSettings() => Log.Info("DEMO: would open Windows night light settings");

    /// <inheritdoc />
    public void OpenDisplaySettings() => Log.Info("DEMO: would open Windows display settings");

    private Task<bool> Set<T>(ref T field, T value)
    {
        lock (Gate)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return Task.FromResult(true);
            field = value;
        }
        Raise(Changed);
        return Task.FromResult(true);
    }
}
