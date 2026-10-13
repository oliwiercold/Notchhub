using NotchHub.Core;

namespace NotchHub.Services.Common;

/// <summary>
/// Raises events so that a throwing subscriber neither breaks the raising code path (timer callbacks, background loops)
/// nor prevents the remaining subscribers from being notified.
/// </summary>
internal static class SafeEvents
{
    public static void Raise(EventHandler? handler, object sender, string context)
    {
        if (handler is null) return;
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(sender, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error($"{context}: event subscriber threw", ex);
            }
        }
    }
}
