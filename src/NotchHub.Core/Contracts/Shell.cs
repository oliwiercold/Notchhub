namespace NotchHub.Core.Contracts;

/// <summary>Tabs of the expanded panel. Settings is not shown in the tab strip (opened from the gear button).</summary>
public enum ShellTab { Controls, Today, Stats, Clipboard, Phone, Power, Settings }

public enum ShellState
{
    /// <summary>Window hidden (e.g. fullscreen app in foreground).</summary>
    Hidden,
    /// <summary>Small notch (optionally with live activity when media plays).</summary>
    Collapsed,
    /// <summary>Hover preview: mini player or glance card.</summary>
    Peek,
    /// <summary>Transient heads-up content (volume, phone notification, timer...).</summary>
    Hud,
    /// <summary>Full panel with tabs.</summary>
    Expanded,
}

public enum HudKind { Volume, Brightness, PhoneNotification, PhonePairRequest, TimerFinished, Info, PhoneCharging, PhoneCall }

/// <summary>Request to briefly show heads-up content in the notch.</summary>
/// <param name="Payload">Kind specific: Volume/Brightness → <see cref="HudLevel"/>; PhoneNotification → <see cref="PhoneNotification"/>;
/// PhonePairRequest → <see cref="PhoneDevice"/>; Info → string; PhoneCharging → <see cref="PhoneBattery"/>; PhoneCall → <see cref="PhoneCall"/>.</param>
public sealed record HudRequest(HudKind Kind, object? Payload = null, TimeSpan? Duration = null);

/// <summary>Payload of volume / brightness HUDs. Level 0..1.</summary>
public sealed record HudLevel(double Level, bool Muted = false, string? Label = null);

/// <summary>Implemented by the notch window (NotchHub.App). Available via <see cref="AppServices.Shell"/>.
/// All members may be called from any thread.</summary>
public interface IShell
{
    ShellState State { get; }
    ShellTab ActiveTab { get; }
    event EventHandler? StateChanged;

    /// <summary>Opens the full panel (optionally on a tab). <paramref name="activate"/> gives it keyboard focus.</summary>
    void Expand(ShellTab? tab = null, bool activate = true);
    void Collapse();
    void Toggle(ShellTab? tab = null);
    void ShowHud(HudRequest request);

    /// <summary>Keeps the expanded panel open (no auto-collapse when the mouse leaves) until the returned token is disposed —
    /// use while a sheet, menu or dialog inside a view is open.</summary>
    IDisposable HoldOpen();
}

/// <summary>Marshals work to the WPF UI thread. Implemented by NotchHub.App.</summary>
public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action);
    Task InvokeAsync(Action action);
    Task<T> InvokeAsync<T>(Func<T> func);
}
