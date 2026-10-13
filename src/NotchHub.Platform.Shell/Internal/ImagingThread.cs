using System.Windows.Threading;
using NotchHub.Core;

namespace NotchHub.Platform.Shell.Internal;

/// <summary>
/// Serial background worker for WPF imaging (decode/encode/scale). WPF imaging objects are DispatcherObjects; running
/// them on pool threads would leave a Dispatcher (and its hidden window) behind on every pool thread they touch.
/// This thread starts on demand, runs work items one at a time and exits after a short idle period,
/// shutting its Dispatcher down so nothing leaks.
/// </summary>
internal static class ImagingThread
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(20);
    private static readonly object Gate = new();
    private static readonly Queue<Action> Work = new();
    private static bool _running;

    /// <summary>Runs <paramref name="work"/> on the imaging thread.</summary>
    public static Task<T> RunAsync<T>(Func<T> work, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ct.IsCancellationRequested)
        {
            tcs.SetCanceled(ct);
            return tcs.Task;
        }

        void Execute()
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }
            try { tcs.TrySetResult(work()); }
            catch (OperationCanceledException oce) { tcs.TrySetCanceled(oce.CancellationToken); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }

        lock (Gate)
        {
            Work.Enqueue(Execute);
            if (_running)
            {
                Monitor.Pulse(Gate);
            }
            else
            {
                StartThreadLocked();
            }
        }
        return tcs.Task;
    }

    private static void StartThreadLocked()
    {
        _running = true;
        var thread = new Thread(Loop) { Name = "NotchHub.Shell.Imaging", IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Loop()
    {
        try
        {
            while (true)
            {
                Action item;
                lock (Gate)
                {
                    while (Work.Count == 0)
                    {
                        if (!Monitor.Wait(Gate, IdleTimeout) && Work.Count == 0)
                        {
                            _running = false;
                            return;
                        }
                    }
                    item = Work.Dequeue();
                }
                item(); // never throws: Execute captures everything into its task
            }
        }
        catch (Exception ex)
        {
            Log.Error("Imaging thread crashed", ex);
            lock (Gate)
            {
                _running = false;
                if (Work.Count > 0) StartThreadLocked();
            }
        }
        finally
        {
            try { Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown(); }
            catch (Exception ex) { Log.Warn("Imaging thread: dispatcher shutdown failed", ex); }
        }
    }
}
