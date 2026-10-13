using NotchHub.Core;
using NotchHub.Demo.Infrastructure;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Common plumbing of the demo services: idempotent start, a lifetime token for background loops,
/// safe event raising and a lock (<see cref="Gate"/>) guarding the in-memory state.
/// </summary>
public abstract class FakeBackgroundService : IBackgroundService
{
    private readonly CancellationTokenSource _lifetime = new();
    private int _started;
    private int _disposed;

    /// <summary>Guards the mutable state of the derived service.</summary>
    protected object Gate { get; } = new();

    /// <summary>Cancelled when the service is disposed.</summary>
    protected CancellationToken Lifetime { get; }

    /// <summary>True after <see cref="Dispose"/>.</summary>
    protected bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    /// <summary>Creates the base and captures the lifetime token.</summary>
    protected FakeBackgroundService() => Lifetime = _lifetime.Token;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed || Interlocked.Exchange(ref _started, 1) == 1) return Task.CompletedTask;
        return OnStartAsync(cancellationToken);
    }

    /// <summary>Start hook (called once): pre-warm generated assets, start loops.</summary>
    protected virtual Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Stop hook (called once from <see cref="Dispose"/>, after the lifetime token was cancelled).</summary>
    protected virtual void OnDispose() { }

    /// <summary>Raises <paramref name="handler"/> on the calling thread, isolating subscriber exceptions.</summary>
    protected void Raise(EventHandler? handler) => DemoEvents.Raise(handler, this, GetType().Name);

    /// <summary>Raises <paramref name="handler"/> on the calling thread, isolating subscriber exceptions.</summary>
    protected void Raise<T>(EventHandler<T>? handler, T args) => DemoEvents.Raise(handler, this, args, GetType().Name);

    /// <summary>
    /// Runs <paramref name="tick"/> after every <paramref name="period"/>() delay until the service is disposed or
    /// <paramref name="cancellationToken"/> is cancelled. Exceptions of a tick are logged and the loop continues.
    /// </summary>
    protected void StartLoop(string name, Func<TimeSpan> period, Action tick, CancellationToken cancellationToken)
    {
        var token = CancellationTokenSource.CreateLinkedTokenSource(Lifetime, cancellationToken);
        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(period(), token.Token).ConfigureAwait(false);
                    try { tick(); }
                    catch (Exception ex) { Log.Error($"Demo {name} loop tick", ex); }
                }
            }
            finally
            {
                token.Dispose();
            }
        }, CancellationToken.None).Forget($"Demo {name} loop");
    }

    /// <summary>
    /// Like <see cref="StartLoop"/>, but ticks are skipped while <see cref="DemoServices.LiveEvents"/> is false,
    /// so spontaneous changes (new notifications, jitter, external volume changes…) stop for deterministic screenshots.
    /// </summary>
    protected void StartLiveLoop(string name, Func<TimeSpan> period, Action tick, CancellationToken cancellationToken) =>
        StartLoop(name, period, () =>
        {
            if (DemoServices.LiveEvents) tick();
        }, cancellationToken);

    /// <summary>Runs <paramref name="work"/> after <paramref name="delay"/> unless the service was disposed meanwhile.</summary>
    protected void After(TimeSpan delay, string name, Action work)
    {
        Task.Run(async () =>
        {
            await Task.Delay(delay, Lifetime).ConfigureAwait(false);
            work();
        }, CancellationToken.None).Forget($"Demo {name}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { _lifetime.Cancel(); }
        catch (Exception ex) { Log.Warn($"{GetType().Name}: cancel failed", ex); }
        Log.Safe($"{GetType().Name}.OnDispose", OnDispose);
        GC.SuppressFinalize(this);
    }
}
