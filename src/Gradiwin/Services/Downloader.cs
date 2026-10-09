// SPDX-License-Identifier: GPL-3.0-or-later
namespace Gradiwin.Services;

/// <summary>Downloads an image dropped as an http(s) URL (DragDropImageLoader does the same).</summary>
public static class Downloader
{
    private static readonly HttpClient s_http = CreateClient();

    public static HttpClient Http => s_http;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Gradiwin/1.14 (+https://github.com/AlexanderVanhee/Gradia)");
        return c;
    }

    public static async Task<string> DownloadImageAsync(Uri uri, string folder)
    {
        using var response = await s_http.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var ext = StdinImageReader.DetectExtension(bytes)
                  ?? throw new InvalidDataException(Localizer.T("The URL does not point to a supported image."));
        var name = Path.GetFileNameWithoutExtension(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(name)) name = "download";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var path = Path.Combine(folder, name + ext);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }
}
