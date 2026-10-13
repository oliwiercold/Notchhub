using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Features.Power;

public sealed partial class PowerTile : ObservableObject
{
    public PowerTile(PowerAction action, string icon, string labelKey, bool needsConfirm)
    {
        Action = action; Icon = icon; LabelKey = labelKey; NeedsConfirm = needsConfirm;
    }

    public PowerAction Action { get; }
    public string Icon { get; }
    public string LabelKey { get; }
    public string Label => NotchHub.UI.Localization.Loc.Get(LabelKey);
    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
    public bool NeedsConfirm { get; }

    /// <summary>First click of a destructive action: waiting for the confirming second click.</summary>
    [ObservableProperty] private bool _isArmed;
    /// <summary>Remaining confirmation time, 1 → 0.</summary>
    [ObservableProperty] private double _countdown;
    [ObservableProperty] private int _secondsLeft;
    [ObservableProperty] private bool _isBusy;
}

public sealed partial class PowerViewModel : ObservableObject
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(4);
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
    private PowerTile? _armed;
    private DateTime _armedAt;

    public PowerViewModel()
    {
        PowerTile[] all =
        [
            new(PowerAction.Lock, "lock", "power.lock", false),
            new(PowerAction.Sleep, "bedtime", "power.sleep", false),
            new(PowerAction.Restart, "restart_alt", "power.restart", true),
            new(PowerAction.Shutdown, "power_settings_new", "power.shutdown", true),
        ];
        var power = AppServices.Power;
        Tiles = new(all.Where(t => power is null || SafeSupported(power, t.Action)));
        _timer.Tick += (_, _) => Tick();
        NotchHub.UI.Localization.Loc.LanguageChanged += (_, _) => { foreach (var t in Tiles) t.RefreshLabel(); };
    }

    public ObservableCollection<PowerTile> Tiles { get; }

    private static bool SafeSupported(IPowerService p, PowerAction a)
    {
        try { return p.IsSupported(a); } catch (Exception ex) { Log.Warn($"IsSupported({a}) failed: {ex.Message}"); return false; }
    }

    [RelayCommand]
    private void Activate(PowerTile? tile)
    {
        if (tile is null || tile.IsBusy) return;
        if (tile.NeedsConfirm && !tile.IsArmed)
        {
            Disarm();
            _armed = tile;
            _armedAt = DateTime.UtcNow;
            tile.Countdown = 1;
            tile.SecondsLeft = (int)ConfirmWindow.TotalSeconds;
            tile.IsArmed = true;
            _timer.Start();
            return;
        }
        Disarm();
        Execute(tile);
    }

    private void Execute(PowerTile tile)
    {
        tile.IsBusy = true;
        if (tile.Action is PowerAction.Lock or PowerAction.Sleep) AppServices.Shell?.Collapse();
        Run().Forget($"power {tile.Action}");

        async Task Run()
        {
            try
            {
                var ok = await AppServices.Power.ExecuteAsync(tile.Action);
                if (!ok) Log.Warn($"Power action {tile.Action} failed");
            }
            finally { AppServices.Ui.Post(() => tile.IsBusy = false); }
        }
    }

    private void Tick()
    {
        if (_armed is null) { _timer.Stop(); return; }
        var left = ConfirmWindow - (DateTime.UtcNow - _armedAt);
        if (left <= TimeSpan.Zero) { Disarm(); return; }
        _armed.Countdown = left / ConfirmWindow;
        _armed.SecondsLeft = (int)Math.Ceiling(left.TotalSeconds);
    }

    private void Disarm()
    {
        _timer.Stop();
        if (_armed is null) return;
        _armed.IsArmed = false;
        _armed.Countdown = 0;
        _armed = null;
    }

    public void OnShown() { }
    public void OnHidden() => Disarm();
}
