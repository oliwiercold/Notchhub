using System.Windows.Threading;
using NotchHub.UI.Localization;
using NotchHub.UI.Modding;

namespace Pomodoro;

public enum Phase { Work, Break }

/// <summary>
/// Example C# mod: a Pomodoro timer. Registers a tab (ring + buttons), a quick-settings tile (remaining time; click = start/pause,
/// chevron = open the tab) and shows a HUD in the notch whenever a phase ends. The 1 s timer runs only while the countdown runs.
/// </summary>
public sealed class PomodoroMod : INotchMod
{
    private IModContext _ctx = null!;
    private ModTile _tile = null!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _endsAt;

    public Phase Phase { get; private set; } = Phase.Work;
    public TimeSpan Remaining { get; private set; }
    public bool IsRunning { get; private set; }
    public int WorkMinutes { get; private set; } = 25;
    public int BreakMinutes { get; private set; } = 5;
    public int CompletedToday { get; private set; }

    public TimeSpan PhaseLength => TimeSpan.FromMinutes(Phase == Phase.Work ? WorkMinutes : BreakMinutes);
    /// <summary>1 → 0 as the phase runs (WavyRing value).</summary>
    public double Fraction => PhaseLength.TotalSeconds <= 0 ? 0 : Math.Clamp(Remaining.TotalSeconds / PhaseLength.TotalSeconds, 0, 1);
    public string RemainingText => $"{(int)Remaining.TotalMinutes:00}:{Remaining.Seconds:00}";

    /// <summary>Raised on the UI thread after any state change (the tab view listens while visible).</summary>
    public event EventHandler? Changed;

    internal static string T(string pl, string en) => Loc.Language == "en" ? en : pl;

    public void Initialize(IModContext context)
    {
        _ctx = context;
        WorkMinutes = Math.Clamp(context.GetSetting("workMinutes", 25), 1, 180);
        BreakMinutes = Math.Clamp(context.GetSetting("breakMinutes", 5), 1, 60);
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        CompletedToday = context.GetSetting("day", "") == today ? context.GetSetting("completed", 0) : 0;
        Remaining = PhaseLength;
        _timer.Tick += (_, _) => Tick();

        context.AddTab(new ModTab
        {
            Id = "pomodoro",
            Title = "Pomodoro",
            Icon = "timer",
            CreateView = () => new PomodoroView(this),
        });
        _tile = new ModTile
        {
            Id = "pomodoro.tile",
            Title = "Pomodoro",
            Icon = "timer",
            OnClick = Toggle,
            OnChevron = () => context.OpenTab("pomodoro"),
        };
        context.AddTile(_tile);
        Publish();
        context.Log($"ready ({WorkMinutes}/{BreakMinutes} min)");
    }

    public void Shutdown() => _timer.Stop();

    // ───────────── commands ─────────────

    public void Toggle()
    {
        if (IsRunning) Pause();
        else Start();
    }

    public void Start()
    {
        if (Remaining <= TimeSpan.Zero) Remaining = PhaseLength;
        _endsAt = DateTime.UtcNow + Remaining;
        IsRunning = true;
        _timer.Start();
        Publish();
    }

    public void Pause()
    {
        if (IsRunning) Remaining = Clamp(_endsAt - DateTime.UtcNow);
        IsRunning = false;
        _timer.Stop();
        Publish();
    }

    public void Reset()
    {
        _timer.Stop();
        IsRunning = false;
        Phase = Phase.Work;
        Remaining = PhaseLength;
        Publish();
    }

    /// <summary>Ends the current phase now (no HUD, no completed count).</summary>
    public void Skip() => SwitchPhase(completed: false);

    public void SetDurations(int work, int brk)
    {
        WorkMinutes = work;
        BreakMinutes = brk;
        _ctx.SetSetting("workMinutes", work);
        _ctx.SetSetting("breakMinutes", brk);
        if (!IsRunning) Remaining = PhaseLength;
        Publish();
    }

    // ───────────── internals ─────────────

    private void Tick()
    {
        Remaining = Clamp(_endsAt - DateTime.UtcNow);
        if (Remaining <= TimeSpan.Zero) SwitchPhase(completed: true);
        else Publish();
    }

    private void SwitchPhase(bool completed)
    {
        var wasWork = Phase == Phase.Work;
        if (completed && wasWork)
        {
            CompletedToday++;
            _ctx.SetSetting("day", DateTime.Today.ToString("yyyy-MM-dd"));
            _ctx.SetSetting("completed", CompletedToday);
        }
        Phase = wasWork ? Phase.Break : Phase.Work;
        Remaining = PhaseLength;
        if (completed)
        {
            _ctx.ShowHud(wasWork
                ? T($"Koniec pracy — {BreakMinutes} min przerwy", $"Focus done — {BreakMinutes} min break")
                : T("Koniec przerwy — wracamy do pracy", "Break over — back to work"), TimeSpan.FromSeconds(4));
        }
        // after work the break starts by itself; after a break wait for the user
        if (wasWork && IsRunning) Start();
        else
        {
            _timer.Stop();
            IsRunning = false;
            Publish();
        }
    }

    private static TimeSpan Clamp(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds));

    private void Publish()
    {
        _tile.IsActive = IsRunning;
        _tile.Icon = Phase == Phase.Work ? "timer" : "coffee";
        _tile.Subtitle = IsRunning
            ? $"{(Phase == Phase.Work ? T("Praca", "Focus") : T("Przerwa", "Break"))} · {RemainingText}"
            : Remaining == PhaseLength ? T($"Start · {RemainingText}", $"Start · {RemainingText}") : T($"Pauza · {RemainingText}", $"Paused · {RemainingText}");
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
