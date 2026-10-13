using System.Text;

namespace NotchHub.Installer.Engine;

/// <summary>Setup log: %TEMP%\NotchHub-setup.log (appended; never throws).</summary>
public static class SetupLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "NotchHub-setup.log");

    public static void Info(string message) => Write("INF", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WRN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(level).Append(' ').Append(message);
        if (ex is not null) line.Append(" :: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        var text = line.ToString();
        System.Diagnostics.Debug.WriteLine(text);
        try
        {
            lock (Gate) File.AppendAllText(FilePath, text + Environment.NewLine);
        }
        catch
        {
            // logging must never throw
        }
    }
}
