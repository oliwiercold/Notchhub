using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Converters;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Stats;

public sealed partial class TopAppItem : ObservableObject
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private byte[]? _icon;
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private string _valueText = "";
}

public sealed partial class StatsViewModel : ObservableObject
{
    private const int TopCount = 6;
    private IDisposable? _live;
    private bool _shown;
    private CancellationTokenSource? _speedCts;

    // CPU
    [ObservableProperty] private double _cpuValue;
    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private string _cpuClock = "";
    [ObservableProperty] private string _cpuProcs = "";
    [ObservableProperty] private string? _cpuTemp;

    // GPU
    [ObservableProperty] private bool _hasGpu;
    [ObservableProperty] private double _gpuValue;
    [ObservableProperty] private string _gpuText = "—";
    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private string _gpuVram = "";
    [ObservableProperty] private string? _gpuTemp;

    // RAM
    [ObservableProperty] private double _ramValue;
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private string _ramDetail = "";
    [ObservableProperty] private string _ramFree = "";

    // Disk / network / uptime
    [ObservableProperty] private string _diskTitle = "";
    [ObservableProperty] private string _diskText = "—";
    [ObservableProperty] private double _diskFraction;
    [ObservableProperty] private string _netDown = "—";
    [ObservableProperty] private string _netUp = "—";
    [ObservableProperty] private string _uptimeText = "—";

    // Top apps
    [ObservableProperty] private bool _sortByCpu;
    [ObservableProperty] private string _topTitle = "";
    [ObservableProperty] private string _sortToggleText = "";
    public ObservableCollection<TopAppItem> TopApps { get; } = new();

    // Speed test
    [ObservableProperty] private bool _speedRunning;
    [ObservableProperty] private bool _speedFailed;
    [ObservableProperty] private bool _hasSpeedResult;
    [ObservableProperty] private string _speedInfo = "";
    [ObservableProperty] private string _speedDown = "—";
    [ObservableProperty] private string _speedUp = "—";
    [ObservableProperty] private string _speedPing = "—";
    [ObservableProperty] private string _speedPhaseText = "";
    [ObservableProperty] private string _speedLive = "0";
    [ObservableProperty] private double _speedProgress;

    public StatsViewModel()
    {
        for (int i = 0; i < TopCount; i++) TopApps.Add(new TopAppItem());
        try { _sortByCpu = string.Equals(AppServices.Settings?.Current.Stats.TopAppsSort, "cpu", StringComparison.OrdinalIgnoreCase); }
        catch { _sortByCpu = false; }
        UpdateSortTexts();
    }

    public void OnShown()
    {
        if (_shown) return;
        _shown = true;
        Loc.LanguageChanged += OnLanguageChanged;
        if (AppServices.Stats is { } stats)
        {
            stats.Updated += OnStatsUpdated;
            _live = stats.RequestLiveUpdates();
        }
        if (AppServices.SpeedTest is { } st) st.Changed += OnSpeedChanged;
        Refresh();
    }

    public void OnHidden()
    {
        if (!_shown) return;
        _shown = false;
        Loc.LanguageChanged -= OnLanguageChanged;
        if (AppServices.Stats is { } stats) stats.Updated -= OnStatsUpdated;
        if (AppServices.SpeedTest is { } st) st.Changed -= OnSpeedChanged;
        _live?.Dispose();
        _live = null;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();
    private void OnStatsUpdated(object? sender, EventArgs e) => AppServices.Ui.Post(() => { if (_shown) ApplyStats(); });
    private void OnSpeedChanged(object? sender, EventArgs e) => AppServices.Ui.Post(() => { if (_shown) ApplySpeed(); });

    private void Refresh()
    {
        UpdateSortTexts();
        ApplyStats();
        ApplySpeed();
    }

    // ───────────── stats ─────────────

    private static string N(double v, string fmt) => v.ToString(fmt, Loc.Culture);
    private static string Pct(double v) => N(Math.Clamp(v, 0, 100), "N0") + "%";

    private void ApplyStats()
    {
        var s = AppServices.Stats?.Latest;
        if (s is null) return;

        var c = s.Cpu;
        CpuValue = Math.Clamp(c.UsagePercent / 100.0, 0, 1);
        CpuText = Pct(c.UsagePercent);
        CpuName = c.Name;
        var ghz = c.CurrentGhz ?? c.BaseGhz;
        CpuClock = ghz > 0 ? N(ghz, "N1") + " GHz" : "";
        CpuProcs = Loc.Format("stats.procs", N(c.ProcessCount, "N0"));
        CpuTemp = c.TemperatureC is { } ct ? N(ct, "N0") + "°C" : null;

        if (s.Gpu is { } g)
        {
            HasGpu = true;
            GpuValue = Math.Clamp(g.UsagePercent / 100.0, 0, 1);
            GpuText = Pct(g.UsagePercent);
            GpuName = g.Name;
            GpuVram = g.VramTotalGb > 0 ? $"{N(g.VramUsedGb, "N1")} / {N(g.VramTotalGb, "N1")} GB" : "";
            GpuTemp = g.TemperatureC is { } gt ? N(gt, "N0") + "°C" : null;
        }
        else
        {
            HasGpu = false;
            GpuValue = 0;
            GpuText = "—";
            GpuName = Loc.Get("stats.gpuUnavailable");
            GpuVram = "";
            GpuTemp = null;
        }

        var m = s.Memory;
        RamValue = Math.Clamp(m.UsagePercent / 100.0, 0, 1);
        RamText = Pct(m.UsagePercent);
        RamDetail = $"{N(m.UsedGb, "N1")} / {N(m.TotalGb, "N1")} GB";
        RamFree = Loc.Format("stats.ramFree", N(m.AvailableGb, "N1") + " GB");

        string want = AppServices.Settings?.Current.Stats.Disk ?? "C:\\";
        var disk = s.Disks.FirstOrDefault(d => string.Equals(d.Name.TrimEnd('\\'), want.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                   ?? s.Disks.FirstOrDefault();
        if (disk is not null)
        {
            DiskTitle = Loc.Format("stats.disk", disk.Name.TrimEnd('\\')).ToUpper(Loc.Culture);
            DiskText = $"{N(disk.UsedGb, "N0")} / {N(disk.TotalGb, "N0")} GB";
            DiskFraction = disk.TotalGb > 0 ? Math.Clamp(disk.UsedGb / disk.TotalGb, 0, 1) : 0;
        }
        else
        {
            DiskTitle = Loc.Format("stats.disk", want.TrimEnd('\\')).ToUpper(Loc.Culture);
            DiskText = "—";
            DiskFraction = 0;
        }

        NetDown = Rate(s.Network.DownBytesPerSec);
        NetUp = Rate(s.Network.UpBytesPerSec);
        UptimeText = Uptime(s.Uptime);

        ApplyTop(SortByCpu ? s.TopByCpu : s.TopByMemory);

        if (HasSpeedResult && !SpeedRunning) UpdateSpeedInfo(); // keeps "5 min ago" fresh
    }

    private void ApplyTop(IReadOnlyList<ProcessGroupStats> list)
    {
        double max = 0;
        for (int i = 0; i < list.Count && i < TopCount; i++)
            max = Math.Max(max, SortByCpu ? list[i].CpuPercent : list[i].MemoryMb);

        for (int i = 0; i < TopCount; i++)
        {
            var item = TopApps[i];
            if (i >= list.Count) { item.IsVisible = false; continue; }
            var p = list[i];
            double v = SortByCpu ? p.CpuPercent : p.MemoryMb;
            item.Name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Key : p.DisplayName;
            item.Icon = p.IconPng is { Length: > 0 } ? p.IconPng : null;
            item.Fraction = max > 0 ? Math.Clamp(v / max, 0, 1) : 0;
            item.ValueText = SortByCpu
                ? N(v, v < 10 ? "N1" : "N0") + "%"
                : v >= 1024 ? N(v / 1024, "N1") + " GB" : N(v, "N0") + " MB";
            item.IsVisible = true;
        }
    }

    private static string Rate(double bps)
    {
        if (!double.IsFinite(bps) || bps < 0) bps = 0;
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        int u = 0;
        while (bps >= 1000 && u < units.Length - 1) { bps /= 1000; u++; }
        string fmt = u == 0 || bps >= 100 ? "N0" : bps >= 10 ? "N0" : "N1";
        return N(bps, fmt) + " " + units[u];
    }

    private static string Uptime(TimeSpan t)
    {
        if (t.TotalDays >= 1) return Loc.Format("stats.uptimeDH", (int)t.TotalDays, t.Hours);
        if (t.TotalHours >= 1) return Loc.Format("stats.uptimeHM", t.Hours, t.Minutes);
        return Loc.Format("stats.uptimeM", Math.Max(0, t.Minutes));
    }

    // ───────────── top apps sort ─────────────

    [RelayCommand]
    private void ToggleSort()
    {
        SortByCpu = !SortByCpu;
        var value = SortByCpu ? "cpu" : "ram";
        try { AppServices.Settings?.Update(s => s.Stats.TopAppsSort = value); }
        catch (Exception ex) { Log.Warn($"stats: saving sort failed: {ex.Message}"); }
        UpdateSortTexts();
        if (AppServices.Stats?.Latest is { } s) ApplyTop(SortByCpu ? s.TopByCpu : s.TopByMemory);
    }

    private void UpdateSortTexts()
    {
        TopTitle = Loc.Format("stats.topApps", SortByCpu ? "CPU" : "RAM");
        SortToggleText = SortByCpu ? "RAM" : "CPU";
    }

    // ───────────── speed test ─────────────

    private void ApplySpeed()
    {
        var st = AppServices.SpeedTest;
        if (st is null) return;
        SpeedRunning = st.IsRunning;
        SpeedFailed = !st.IsRunning && st.Phase == SpeedTestPhase.Failed;
        SpeedProgress = Math.Clamp(st.Progress, 0, 1);
        SpeedLive = N(st.CurrentMbps, st.CurrentMbps < 10 ? "N1" : "N0");
        SpeedPhaseText = st.Phase switch
        {
            SpeedTestPhase.Latency => Loc.Get("stats.phaseLatency"),
            SpeedTestPhase.Download => Loc.Get("stats.phaseDownload"),
            SpeedTestPhase.Upload => Loc.Get("stats.phaseUpload"),
            _ => Loc.Get("stats.phaseStarting"),
        };

        if (st.LastResult is { } r)
        {
            HasSpeedResult = true;
            SpeedDown = N(r.DownloadMbps, r.DownloadMbps < 10 ? "N1" : "N0");
            SpeedUp = N(r.UploadMbps, r.UploadMbps < 10 ? "N1" : "N0");
            SpeedPing = N(r.LatencyMs, "N0");
        }
        else
        {
            HasSpeedResult = false;
            SpeedDown = SpeedUp = SpeedPing = "—";
        }
        UpdateSpeedInfo();
    }

    private void UpdateSpeedInfo()
    {
        var r = AppServices.SpeedTest?.LastResult;
        if (r is null) { SpeedInfo = Loc.Get("stats.speedNever"); return; }
        var info = Loc.Format("stats.speedLast", RelativeTime.Describe(r.At));
        if (!string.IsNullOrWhiteSpace(r.ServerLocation)) info += " • " + r.ServerLocation;
        SpeedInfo = info;
    }

    [RelayCommand]
    private void RunSpeedTest()
    {
        var st = AppServices.SpeedTest;
        if (st is null || st.IsRunning) return;
        _speedCts?.Dispose();
        _speedCts = new CancellationTokenSource();
        SpeedRunning = true;
        SpeedFailed = false;
        SpeedProgress = 0;
        SpeedLive = "0";
        SpeedPhaseText = Loc.Get("stats.phaseStarting");
        st.RunAsync(_speedCts.Token).Forget("stats speed test");
    }

    [RelayCommand]
    private void CancelSpeedTest()
    {
        try { _speedCts?.Cancel(); } catch (ObjectDisposedException) { }
        AppServices.SpeedTest?.Cancel();
    }
}
