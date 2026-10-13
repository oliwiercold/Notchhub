using System.Collections.Concurrent;
using NotchHub.Core;

namespace NotchHub.Demo.Infrastructure;

/// <summary>
/// One dedicated, long-lived background STA thread for all WPF imaging work of the demo
/// (DrawingVisual / FormattedText / RenderTargetBitmap require an STA thread).
/// Work items run sequentially; callers get a <see cref="Task{TResult}"/> and never block the UI thread.
/// </summary>
internal static class StaWorker
{
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly Lazy<Thread> WorkerThread = new(StartThread, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>True when the caller already runs on the imaging thread (blocking on queued work would deadlock).</summary>
    public static bool IsCurrentThread => WorkerThread.IsValueCreated && Thread.CurrentThread == WorkerThread.Value;

    /// <summary>Queues <paramref name="work"/> on the STA imaging thread.</summary>
    public static Task<T> InvokeAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (IsCurrentThread)
        {
            try { return Task.FromResult(work()); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = WorkerThread.Value;
        Queue.Add(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    private static Thread StartThread()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "NotchHub.Demo imaging (STA)" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    private static void Run()
    {
        foreach (var work in Queue.GetConsumingEnumerable())
        {
            // Work items complete their own TaskCompletionSource; this is a last-resort guard.
            try { work(); }
            catch (Exception ex) { Log.Error("Demo imaging worker", ex); }
        }
    }
}
