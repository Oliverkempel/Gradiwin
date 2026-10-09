// SPDX-License-Identifier: GPL-3.0-or-later
namespace Gradiwin.Services;

/// <summary>Tiny diagnostic log (port of backend/logger.py) at %TEMP%\Gradiwin\gradiwin.log.</summary>
public static class Log
{
    private static readonly object s_lock = new();
    private static readonly string s_path = Path.Combine(Path.GetTempPath(), "Gradiwin", "gradiwin.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? e = null) => Write("ERROR", e is null ? message : $"{message}: {e}");

    private static void Write(string level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        try
        {
            lock (s_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(s_path)!);
                var info = new FileInfo(s_path);
                if (info.Exists && info.Length > 1_000_000)
                    info.Delete();
                File.AppendAllText(s_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never throw.
        }
    }
}
