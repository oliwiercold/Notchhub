using System.Collections.Concurrent;
using NotchHub.Core;

namespace NotchHub.Platform.Media.Audio;

/// <summary>
/// Dedicated MTA thread that owns every Core Audio COM object of the <see cref="AudioService"/>. All COM calls and
/// all state mutations run here, one at a time, so the service needs no locking around COM state and COM callbacks
/// (which arrive on arbitrary MTA threads) only have to <see cref="Post"/> work.
/// </summary>
internal sealed class AudioWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private int _disposed;

    public AudioWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>True when called from the worker thread itself.</summary>
    public bool IsCurrentThread => Thread.CurrentThread == _thread;

    /// <summary>Queues <paramref name="action"/>; returns false after disposal. Never blocks.</summary>
    public bool Post(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        try
        {
            _queue.Add(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // CompleteAdding raced with us
        }
    }

    /// <summary>Runs <paramref name="func"/> on the worker; runs inline when already on it (no self-deadlock).</summary>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        if (IsCurrentThread)
        {
            try { return Task.FromResult(func()); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = Post(() =>
        {
            try { tcs.TrySetResult(func()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        if (!posted) tcs.TrySetCanceled();
        return tcs.Task;
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("AudioWorker: work item failed", ex);
            }
        }
    }

    /// <summary>
    /// Stops accepting work, lets already queued items (including the caller's teardown item) finish and waits
    /// briefly for the thread to exit.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.CompleteAdding();
        if (!IsCurrentThread && !_thread.Join(TimeSpan.FromSeconds(2)))
            Log.Warn("AudioWorker: thread did not stop within 2 s");
        // The queue is intentionally not disposed: a racing Post() may still touch it; it holds no unmanaged state.
    }
}
