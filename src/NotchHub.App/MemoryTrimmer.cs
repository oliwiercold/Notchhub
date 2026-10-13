using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.App;

/// <summary>
/// Keeps the resident footprint small for an app that sits idle most of the time: some seconds after the panel
/// collapses (and once after startup) it compacts the managed heap and lets Windows trim the working set.
/// Pages come back on demand, so this costs nothing while the panel is being used.
/// </summary>
internal sealed class MemoryTrimmer
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(8);
    private readonly IShell _shell;
    private readonly DispatcherTimer _timer;

    public MemoryTrimmer(IShell shell, Dispatcher dispatcher)
    {
        _shell = shell;
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dispatcher) { Interval = TimeSpan.FromSeconds(12) };
        _timer.Tick += (_, _) => { _timer.Stop(); Trim(); };
        shell.StateChanged += (_, _) => dispatcher.BeginInvoke(OnStateChanged);
        _timer.Start(); // once after startup
    }

    private void OnStateChanged()
    {
        _timer.Stop();
        if (_shell.State is ShellState.Collapsed or ShellState.Hidden)
        {
            _timer.Interval = IdleDelay;
            _timer.Start();
        }
    }

    private void Trim()
    {
        if (_shell.State is not (ShellState.Collapsed or ShellState.Hidden)) return;
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }
        catch (Exception ex)
        {
            Log.Debug("MemoryTrimmer: " + ex.Message);
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
