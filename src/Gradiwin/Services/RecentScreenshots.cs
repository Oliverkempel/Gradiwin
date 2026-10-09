// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace Gradiwin.Services;

/// <summary>Port of ui/recent_picker.py RecentImageGetter.</summary>
public static class RecentScreenshots
{
    private static readonly string[] s_extensions = [".png", ".jpg", ".jpeg", ".webp", ".avif", ".bmp"];

    public static string ResolveFolder()
    {
        var configured = SettingsService.Instance.ScreenshotFolder;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        // Windows' Snipping Tool / Win+PrtScn save to Pictures\Screenshots (localized display name, fixed folder).
        var screenshots = Path.Combine(pictures, "Screenshots");
        if (Directory.Exists(screenshots))
            return screenshots;

        foreach (var name in LocalizedFolderNames())
        {
            var candidate = Path.Combine(pictures, name);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return pictures;
    }

    private static IEnumerable<string> LocalizedFolderNames()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "screenshot_folder_names.json");
        if (!File.Exists(file)) yield break;
        Dictionary<string, string>? names = null;
        try
        {
            names = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
        }
        catch (JsonException)
        {
            // Ignore.
        }
        if (names == null) yield break;
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (names.TryGetValue(lang, out var localized)) yield return localized;
        foreach (var v in names.Values.Distinct()) yield return v;
    }

    public static IReadOnlyList<FileInfo> Get(int count = 6)
    {
        try
        {
            var dir = new DirectoryInfo(ResolveFolder());
            return dir.EnumerateFiles()
                .Where(f => s_extensions.Contains(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(count)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
