// SPDX-License-Identifier: GPL-3.0-or-later
namespace Gradiwin.Services;

/// <summary>Per-window temp directory (Gradia creates one per window and removes it at shutdown).</summary>
public sealed class SessionFiles : IDisposable
{
    public SessionFiles()
    {
        Folder = Path.Combine(Path.GetTempPath(), "Gradiwin", "session-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    public string NewPath(string name) => Path.Combine(Folder, name);

    public string NewUniquePath(string prefix, string extension) =>
        Path.Combine(Folder, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}{extension}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Files may still be in use (clipboard); leave them for the OS temp cleaner.
        }
    }

    /// <summary>Removes leftovers of previous runs older than a day.</summary>
    public static void CleanupStale()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "Gradiwin");
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root, "session-*"))
            {
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                    Directory.Delete(dir, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Ignore.
        }
    }
}
