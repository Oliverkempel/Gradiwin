// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualBasic.FileIO;

namespace Gradiwin.Services;

/// <summary>Moves files to the Recycle Bin (Gradia "trashes" screenshots via GIO).</summary>
public static class RecycleBin
{
    public static int Recycle(IEnumerable<string> paths)
    {
        int count = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path)) continue;
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                count++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // Skip files we can't move.
            }
        }
        return count;
    }
}
