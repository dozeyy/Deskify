using System.IO;

namespace Deskify.Services;

/// <summary>Best-effort file log. Never throws, never blocks the UI meaningfully.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deskify", "deskify.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
