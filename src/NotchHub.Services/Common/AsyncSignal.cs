namespace NotchHub.Services.Common;

/// <summary>
/// Auto-reset wake-up signal for background loops: <see cref="WaitAsync"/> completes when <see cref="Set"/> is called
/// or the timeout elapses (measured with the injected <see cref="TimeProvider"/>, so tests can drive it).
/// A <see cref="Set"/> that happens while nobody waits is remembered for the next wait.
/// </summary>
internal sealed class AsyncSignal
{
    private TaskCompletionSource _signal = NewSource();

    public void Set() => Volatile.Read(ref _signal).TrySetResult();

    /// <returns>True when signalled, false on timeout.</returns>
    public async Task<bool> WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken ct)
    {
        var current = Volatile.Read(ref _signal);
        if (!current.Task.IsCompleted)
        {
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delay = Task.Delay(timeout, time, delayCts.Token);
            await Task.WhenAny(current.Task, delay).ConfigureAwait(false);
            delayCts.Cancel(); // release the delay timer when the signal won
            ct.ThrowIfCancellationRequested();
        }

        if (!current.Task.IsCompleted) return false;
        // Reset only if nobody replaced the source in between; a Set() racing with this lands on the new source.
        Interlocked.CompareExchange(ref _signal, NewSource(), current);
        return true;
    }

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
