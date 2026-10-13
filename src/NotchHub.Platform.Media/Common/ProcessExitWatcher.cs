using Microsoft.Win32.SafeHandles;
using NotchHub.Core;

namespace NotchHub.Platform.Media.Common;

/// <summary>
/// Invokes a callback (on the thread pool) once a process exits. Uses only SYNCHRONIZE access, so it also works for
/// elevated processes of the same user. Dispose to stop watching.
/// </summary>
internal sealed class ProcessExitWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly SafeProcessHandle _process;
    private readonly ProcessWaitHandle _waitHandle;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private ProcessExitWatcher(SafeProcessHandle process, Action onExit, string context)
    {
        _process = process;
        _waitHandle = new ProcessWaitHandle(process);
        _registration = ThreadPool.RegisterWaitForSingleObject(_waitHandle, (_, _) =>
        {
            lock (_gate)
            {
                if (_disposed) return;
            }
            Log.Safe(context, onExit);
        }, null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Starts watching; null when the process cannot be opened (already gone or protected).</summary>
    public static ProcessExitWatcher? TryWatch(uint processId, Action onExit, string context)
    {
        if (processId == 0) return null;
        var handle = Win32.OpenProcess(Win32.SYNCHRONIZE, false, processId);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return new ProcessExitWatcher(handle, onExit, context);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _registration?.Unregister(null);
            _registration = null;
        }
        _waitHandle.Dispose();
        _process.Dispose();
    }

    /// <summary>WaitHandle view of a process handle; the handle itself stays owned by the watcher.</summary>
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle process)
        {
            SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
        }
    }
}
