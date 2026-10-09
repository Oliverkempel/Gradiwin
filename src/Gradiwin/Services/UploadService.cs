// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gradiwin.Services;

public sealed record ProviderFeature(string Text, string Type);

/// <summary>Upload provider metadata (from Gradia's web/upload-providers JSON, minus the bash commands).</summary>
public sealed record UploadProvider(
    string Id,
    string Name,
    string Description,
    string Details,
    string HomepageUrl,
    string TosUrl,
    IReadOnlyList<ProviderFeature> Features);

public sealed record UploadResult(bool Success, string Output, string? Warning = null)
{
    public bool IsUrl => Output.StartsWith("http://", StringComparison.Ordinal) || Output.StartsWith("https://", StringComparison.Ordinal);
}

/// <summary>
/// Port of CommandLineExporter. Built-in providers upload natively with HttpClient;
/// a custom command runs through PowerShell with $1 replaced by the image path.
/// </summary>
public static class UploadService
{
    public const string CustomProviderId = "custom";

    private static IReadOnlyList<UploadProvider>? s_providers;

    public static IReadOnlyList<UploadProvider> Providers => s_providers ??= LoadProviders();

    private static IReadOnlyList<UploadProvider> LoadProviders()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "providers.json");
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            return root.Select(kv =>
            {
                var o = kv.Value!.AsObject();
                var features = (o["features"] as JsonArray ?? [])
                    .Select(f => new ProviderFeature(f!["text"]!.GetValue<string>(), f["type"]!.GetValue<string>()))
                    .ToList();
                return new UploadProvider(kv.Key,
                    o["name"]?.GetValue<string>() ?? kv.Key,
                    o["description"]?.GetValue<string>() ?? "",
                    o["details"]?.GetValue<string>() ?? "",
                    o["homepage_url"]?.GetValue<string>() ?? "",
                    o["tos_url"]?.GetValue<string>() ?? "",
                    features);
            }).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or NullReferenceException)
        {
            return [];
        }
    }

    public static bool IsConfigured
    {
        get
        {
            var s = SettingsService.Instance;
            return s.ProviderName switch
            {
                "" => false,
                CustomProviderId => s.CustomExportCommand.Contains("$1", StringComparison.Ordinal),
                "imgur" => !string.IsNullOrWhiteSpace(s.ImgurClientId),
                _ => true,
            };
        }
    }

    public static string? ProviderDisplayName
    {
        get
        {
            var id = SettingsService.Instance.ProviderName;
            if (id == CustomProviderId) return Localizer.T("Custom");
            return Providers.FirstOrDefault(p => p.Id == id)?.Name;
        }
    }

    public static async Task<UploadResult> UploadAsync(string imagePath, CancellationToken ct = default)
    {
        var s = SettingsService.Instance;
        return s.ProviderName switch
        {
            "imgur" => await UploadImgurAsync(imagePath, s.ImgurClientId, ct),
            "gofile" => await UploadGoFileAsync(imagePath, ct),
            CustomProviderId => await RunCustomCommandAsync(s.CustomExportCommand, imagePath, ct),
            _ => new UploadResult(false, Localizer.T("No upload provider selected")),
        };
    }

    private static MultipartFormDataContent FileContent(string field, string path)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(File.ReadAllBytes(path));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, field, Path.GetFileName(path));
        return form;
    }

    private static async Task<UploadResult> UploadImgurAsync(string path, string clientId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.imgur.com/3/image")
        {
            Content = FileContent("image", path),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", clientId);
        using var resp = await Downloader.Http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        var data = JsonNode.Parse(json)?["data"];
        var link = data?["link"]?.GetValue<string>();
        if (!resp.IsSuccessStatusCode || link is null)
            return new UploadResult(false, data?["error"]?.ToString() ?? resp.ReasonPhrase ?? "Imgur upload failed");

        // Keep delete hashes like Gradia's imgur-delete-hashes.txt.
        if (data?["deletehash"]?.GetValue<string>() is { } hash)
        {
            var log = Path.Combine(SettingsService.DataFolder, "imgur-delete-hashes.txt");
            await File.AppendAllTextAsync(log, $"[{DateTime.Now:yyyy-MM-dd_HH:mm:ss}] {hash} {link}{Environment.NewLine}", ct);
        }
        return new UploadResult(true, link);
    }

    private static async Task<UploadResult> UploadGoFileAsync(string path, CancellationToken ct)
    {
        using var resp = await Downloader.Http.PostAsync("https://upload.gofile.io/uploadfile", FileContent("file", path), ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        var page = JsonNode.Parse(json)?["data"]?["downloadPage"]?.GetValue<string>();
        return page is null
            ? new UploadResult(false, resp.IsSuccessStatusCode ? json : resp.ReasonPhrase ?? "GoFile upload failed")
            : new UploadResult(true, page);
    }

    /// <summary>Runs the user's command (Gradia runs it with bash -c; here PowerShell).</summary>
    public static async Task<UploadResult> RunCustomCommandAsync(string template, string imagePath, CancellationToken ct)
    {
        if (!template.Contains("$1", StringComparison.Ordinal))
            return new UploadResult(false, Localizer.T("Custom export command must include $1 as a placeholder for the image path"));

        var command = template.Replace("$1", "'" + imagePath.Replace("'", "''") + "'", StringComparison.Ordinal);
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode != 0)
            return new UploadResult(false, Localizer.T("Custom command failed: ") + (stderr.Length > 0 ? stderr : "exit code " + process.ExitCode));
        return new UploadResult(true, stdout, stderr.Length > 0 ? stderr : null);
    }
}
