using NotchHub.Core;

namespace NotchHub.Demo.Infrastructure;

/// <summary>Raises events subscriber by subscriber so one faulty handler can neither break the others nor the caller.</summary>
internal static class DemoEvents
{
    public static void Raise(EventHandler? handler, object sender, string context)
    {
        if (handler is null) return;
        foreach (var subscriber in handler.GetInvocationList())
        {
            try { ((EventHandler)subscriber)(sender, EventArgs.Empty); }
            catch (Exception ex) { Log.Error($"{context}: event handler failed", ex); }
        }
    }

    public static void Raise<T>(EventHandler<T>? handler, object sender, T args, string context)
    {
        if (handler is null) return;
        foreach (var subscriber in handler.GetInvocationList())
        {
            try { ((EventHandler<T>)subscriber)(sender, args); }
            catch (Exception ex) { Log.Error($"{context}: event handler failed", ex); }
        }
    }
}
