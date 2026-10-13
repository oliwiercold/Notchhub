using System.Security.Cryptography;
using System.Text;

namespace NotchHub.App;

/// <summary>
/// One instance per data directory (named mutex). A second launch signals the first (named event) to expand, then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle signal)
    {
        _mutex = mutex;
        _signal = signal;
    }

    /// <summary>Returns the instance guard, or null when another instance owns <paramref name="dataRoot"/> (it was signalled).</summary>
    public static SingleInstance? TryAcquire(string dataRoot, Action onActivate)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataRoot.ToUpperInvariant())))[..12];
        var mutex = new Mutex(true, $@"Local\NotchHub.Instance.{hash}", out var created);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\NotchHub.Activate.{hash}");
        if (!created)
        {
            signal.Set();
            signal.Dispose();
            mutex.Dispose();
            return null;
        }
        var instance = new SingleInstance(mutex, signal);
        instance._wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => onActivate(), null, Timeout.Infinite, false);
        return instance;
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _signal.Dispose();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
