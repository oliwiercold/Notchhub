using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NotchHub.App.Shell;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.App;

/// <summary>
/// Keeps the resident footprint small for an app that sits idle most of the time:
/// <list type="bullet">
/// <item>some seconds after the panel collapses (and once after startup) it compacts the managed heap and lets Windows
/// trim the working set;</item>
/// <item>while the panel stays collapsed it wakes up every few minutes: tab views that have not been on screen for
/// <see cref="ReleaseAfter"/> are released (re-created lazily on the next visit), and when anything was released or
/// the app allocated since the last pass the heap (LOH included) is compacted and the working set trimmed again.</item>
/// </list>
/// Pages come back on demand, so this costs nothing while the panel is being used. No timer runs while it is open.
/// </summary>
internal sealed class MemoryTrimmer
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(3);
    /// <summary>Allocations below this since the last pass are not worth another full compaction.</summary>
    private const long MinAllocatedForRetrim = 2 * 1024 * 1024;

    private readonly NotchWindow _shell;
    private readonly DispatcherTimer _timer;
    private long _allocatedAtTrim = -1;

    public MemoryTrimmer(NotchWindow shell, Dispatcher dispatcher, TimeSpan? releaseAfter = null)
    {
        _shell = shell;
        ReleaseAfter = releaseAfter ?? TimeSpan.FromMinutes(2);
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dispatcher) { Interval = TimeSpan.FromSeconds(12) };
        _timer.Tick += (_, _) => OnTick();
        ((IShell)shell).StateChanged += (_, _) => dispatcher.BeginInvoke(OnStateChanged);
        _timer.Start(); // once after startup
    }

    /// <summary>How long a tab view must have been off screen (panel collapsed) before it is released.</summary>
    public TimeSpan ReleaseAfter { get; }

    private static bool IsIdle(ShellState s) => s is ShellState.Collapsed or ShellState.Hidden;

    private void OnStateChanged()
    {
        _timer.Stop();
        if (!IsIdle(_shell.State)) return;
        _timer.Interval = IdleDelay;
        _timer.Start();
    }

    private void OnTick()
    {
        _timer.Stop();
        if (!IsIdle(_shell.State)) return;
        var first = _timer.Interval == IdleDelay || _allocatedAtTrim < 0;

        var released = 0;
        try { released = _shell.ReleaseIdleTabs(ReleaseAfter); }
        catch (Exception ex) { Log.Debug("MemoryTrimmer release: " + ex.Message); }

        if (first || released > 0 || GC.GetTotalAllocatedBytes() - _allocatedAtTrim >= MinAllocatedForRetrim) Trim();

        // first periodic pass right after the release threshold, then every few minutes (cheap: one check, no allocations)
        _timer.Interval = first && ReleaseAfter < Period ? ReleaseAfter + TimeSpan.FromSeconds(5) : Period;
        _timer.Start();
    }

    private void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            // Aggressive: compacts again (objects freed by the finalizers above) and decommits the free regions the GC
            // would otherwise keep committed for future allocations (about 2-3x the live heap after the panel was used)
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            _allocatedAtTrim = GC.GetTotalAllocatedBytes();
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
            if (Log.Verbose)
            {
                var info = GC.GetGCMemoryInfo();
                Log.Debug($"MemoryTrimmer: heap {info.HeapSizeBytes / 1048576.0:F1} MB, GC committed {info.TotalCommittedBytes / 1048576.0:F1} MB, " +
                          $"working set {Environment.WorkingSet / 1048576.0:F1} MB");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MemoryTrimmer: " + ex.Message);
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
