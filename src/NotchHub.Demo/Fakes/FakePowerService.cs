using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Demo.Fakes;

/// <summary>Demo power actions: everything is "supported", nothing is ever executed (only logged). Never calls an OS API.</summary>
public sealed class FakePowerService : IPowerService
{
    /// <inheritdoc />
    public bool IsSupported(PowerAction action) => true;

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(PowerAction action)
    {
        Log.Info($"DEMO: would execute {action}");
        return Task.FromResult(true);
    }
}
