using System.Diagnostics;
using System.Text;

namespace NotchHub.Core;

/// <summary>Tiny thread-safe file logger: %APPDATA%\NotchHub\logs\notchhub-yyyyMMdd.log</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static bool Verbose { get; set; }

    public static void Debug(string message)
    {
        if (Verbose) Write("DBG", message, null);
    }

    public static void Info(string message) => Write("INF", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WRN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ')
            .Append(level).Append(" [").Append(Environment.CurrentManagedThreadId).Append("] ")
            .Append(message);
        if (ex is not null) line.Append(" :: ").Append(ex);
        var text = line.ToString();
        System.Diagnostics.Debug.WriteLine(text);
        try
        {
            lock (Gate)
            {
                var file = Path.Combine(AppPaths.Logs, $"notchhub-{DateTime.Now:yyyyMMdd}.log");
                System.IO.File.AppendAllText(file, text + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never throw
        }
    }

    /// <summary>Runs an action, logging (not rethrowing) any exception.</summary>
    public static void Safe(string context, Action action)
    {
        try { action(); }
        catch (Exception ex) { Error(context, ex); }
    }

    /// <summary>Fire-and-forget helper that logs task failures.</summary>
    public static async void Forget(this Task task, string context)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error(context, ex); }
    }

    [Conditional("DEBUG")]
    public static void Trace(string message) => Write("TRC", message, null);
}
