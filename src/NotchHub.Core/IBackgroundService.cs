namespace NotchHub.Core;

/// <summary>
/// Long-lived service with an explicit start. <see cref="IDisposable.Dispose"/> stops it.
/// Conventions for every service in NotchHub:
///  * StartAsync must not throw when a feature is unavailable on this machine — degrade gracefully, log, expose IsAvailable-style flags.
///  * Events may be raised on ANY thread. Consumers (view models) marshal to the UI thread themselves.
///  * Public methods are thread-safe and never block the caller for long (use async for I/O).
///  * State properties return immutable snapshots (records / read-only lists) so they can be read from any thread.
/// </summary>
public interface IBackgroundService : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
}
