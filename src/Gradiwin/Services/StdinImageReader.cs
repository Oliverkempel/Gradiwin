// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Gradiwin.Services;

/// <summary>Port of utils/std_image_loader.py: accept an image piped on stdin.</summary>
public static partial class StdinImageReader
{
    private const int FILE_TYPE_DISK = 0x0001;
    private const int FILE_TYPE_PIPE = 0x0003;
    private const int STD_INPUT_HANDLE = -10;

    public static string InboxFolder
    {
        get
        {
            var dir = Path.Combine(Path.GetTempPath(), "Gradiwin", "inbox");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Reads stdin if it is a pipe/file containing image bytes; returns the saved path.</summary>
    public static string? TryReadImageToInbox()
    {
        try
        {
            var handle = GetStdHandle(STD_INPUT_HANDLE);
            if (handle == 0 || handle == -1)
                return null;
            int type = GetFileType(handle);
            if (type != FILE_TYPE_PIPE && type != FILE_TYPE_DISK)
                return null;

            using var stdin = Console.OpenStandardInput();
            using var ms = new MemoryStream();
            stdin.CopyTo(ms);
            var bytes = ms.ToArray();
            var ext = DetectExtension(bytes);
            if (ext == null)
                return null;

            var path = Path.Combine(InboxFolder, $"stdin-{DateTime.Now:yyyyMMdd-HHmmss-fff}{ext}");
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Takes every pending inbox file (oldest first).</summary>
    public static IReadOnlyList<string> DrainInbox()
    {
        try
        {
            return Directory.GetFiles(InboxFolder).OrderBy(File.GetCreationTimeUtc).ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }

    public static string? DetectExtension(ReadOnlySpan<byte> b)
    {
        if (b.Length < 12) return null;
        if (b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') return ".png";
        if (b[0] == 0xFF && b[1] == 0xD8) return ".jpg";
        if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        if (b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p' && b[8] == 'a' && b[9] == 'v' && b[10] == 'i') return ".avif";
        if (b[0] == 'B' && b[1] == 'M') return ".bmp";
        if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return ".gif";
        return null;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll")]
    private static partial int GetFileType(nint hFile);
}
