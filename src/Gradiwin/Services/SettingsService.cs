// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;

namespace Gradiwin.Services;

/// <summary>
/// Persistent settings. Keys and value shapes mirror Gradia's GSettings schema
/// (be.alexandervanhee.gradia.gschema.xml). Stored as JSON in the package's LocalFolder.
/// </summary>
public sealed class SettingsService
{
    private static readonly Lazy<SettingsService> s_instance = new(() => new SettingsService());
    public static SettingsService Instance => s_instance.Value;

    private readonly string _path;
    private readonly JsonObject _data;
    private readonly object _lock = new();
    private System.Threading.Timer? _saveTimer;

    public event Action<string>? Changed;

    private SettingsService()
    {
        string folder;
        try
        {
            folder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            // Unpackaged (no package identity): fall back to %LOCALAPPDATA%.
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gradiwin");
        }
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
        _data = Load(_path);
    }

    public static string DataFolder => Path.GetDirectoryName(Instance._path)!;

    private static JsonObject Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
                return o;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // Corrupt settings: start fresh.
        }
        return new JsonObject();
    }

    private T Get<T>(string key, T fallback)
    {
        lock (_lock)
        {
            try
            {
                if (_data[key] is JsonNode node)
                    return node.GetValue<T>();
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException)
            {
                // Wrong type stored; fall back.
            }
            return fallback;
        }
    }

    private void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            var existing = _data[key];
            var node = JsonValue.Create(value);
            if (existing != null && JsonNode.DeepEquals(existing, node))
                return;
            _data[key] = node;
        }
        ScheduleSave();
        Changed?.Invoke(key);
    }

    private void ScheduleSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(_ => Flush(), null, 300, Timeout.Infinite);
    }

    public void Flush()
    {
        string json;
        lock (_lock)
            json = _data.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (IOException)
        {
            // Best effort; will retry on next change.
        }
    }

    // ---- Drawing ----
    public DrawingMode DrawMode
    {
        get => DrawingModeExtensions.FromKey(Get("draw-mode", "SELECT"));
        set => Set("draw-mode", value.ToKey());
    }

    public ToolOptions GetToolOptions(DrawingMode mode)
    {
        var json = Get<string?>($"tool-config/tool_{mode.ToKey().ToLowerInvariant()}", null);
        if (json != null)
        {
            try
            {
                var o = ToolOptions.Deserialize(json);
                o.Mode = mode;
                return o;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                // Fall through to defaults.
            }
        }
        return ToolOptions.CreateDefault(mode);
    }

    public void SetToolOptions(ToolOptions options) =>
        Set($"tool-config/tool_{options.Mode.ToKey().ToLowerInvariant()}", options.Serialize());

    // ---- Image options ----
    public int ImagePadding { get => Get("image-padding", 5); set => Set("image-padding", value); }
    public int ImageCornerRadius { get => Get("image-corner-radius", 2); set => Set("image-corner-radius", value); }
    public string ImageAspectRatio { get => Get("image-aspect-ratio", ""); set => Set("image-aspect-ratio", value); }
    public int ImageShadowStrength { get => Get("image-shadow-strength", 5); set => Set("image-shadow-strength", value); }
    public bool ImageAutoBalance { get => Get("image-auto-balance", false); set => Set("image-auto-balance", value); }
    public bool ImageOptionsLock { get => Get("image-options-lock", false); set => Set("image-options-lock", value); }

    // ---- Background ----
    public BackgroundMode BackgroundMode
    {
        get => BackgroundModeExtensions.FromKey(Get("background-mode", "none"));
        set => Set("background-mode", value.ToKey());
    }

    public SolidBackground SolidState
    {
        get => SolidBackground.FromJson(Get("solid-state", new SolidBackground().ToJson()));
        set => Set("solid-state", value.ToJson());
    }

    public Gradient GradientState
    {
        get => Gradient.FromJson(Get("gradient-state", new Gradient().ToJson()));
        set => Set("gradient-state", value.ToJson());
    }

    public ImageBackgroundState ImageState
    {
        get => ImageBackgroundState.FromJson(Get("image-state", ""));
        set => Set("image-state", value.ToJson());
    }

    // ---- Screenshots / export ----
    public string ScreenshotFolder { get => Get("screenshot-folder", ""); set => Set("screenshot-folder", value); }
    public string ExportFormat { get => Get("export-format", "png"); set => Set("export-format", value); }
    public bool ExportCompress { get => Get("export-compress", true); set => Set("export-compress", value); }
    public bool TrashScreenshotsOnClose { get => Get("trash-screenshots-on-close", false); set => Set("trash-screenshots-on-close", value); }

    /// <summary>"confirm", "copy" or "none".</summary>
    public string ExitMethod { get => Get("exit-method", "confirm"); set => Set("exit-method", value); }
    public bool OverwriteScreenshot { get => Get("overwrite-screenshot", false); set => Set("overwrite-screenshot", value); }

    // ---- Upload ----
    public string CustomExportCommand { get => Get("custom-export-command", ""); set => Set("custom-export-command", value); }
    public string ProviderName { get => Get("provider-name", ""); set => Set("provider-name", value); }
    public bool ShowExportConfirmDialog { get => Get("show-export-confirm-dialog", true); set => Set("show-export-confirm-dialog", value); }
    public string ImgurClientId { get => Get("imgur-client-id", ""); set => Set("imgur-client-id", value); }

    // ---- Source snippets ----
    public bool SourceSnippetShowFrame { get => Get("source-snippet-show-frame", true); set => Set("source-snippet-show-frame", value); }
    public bool SourceSnippetShowLineNumbers { get => Get("source-snippet-show-line-numbers", true); set => Set("source-snippet-show-line-numbers", value); }
    public string SourceSnippetLanguage { get => Get("source-snippet-language", "c"); set => Set("source-snippet-language", value); }
    public string SourceSnippetCodeText { get => Get("source-snippet-code-text", DefaultSnippet); set => Set("source-snippet-code-text", value); }
    public string SourceSnippetTitle { get => Get("source-snippet-title", "My Code"); set => Set("source-snippet-title", value); }
    public string SourceSnippetStyleScheme { get => Get("source-snippet-style-scheme", "Dark"); set => Set("source-snippet-style-scheme", value); }
    public double SourceSnippetWidth { get => Get("source-snippet-width", 640.0); set => Set("source-snippet-width", value); }

    // ---- OCR ----
    /// <summary>BCP-47 tag of the OCR language; empty = user profile languages.</summary>
    public string TrainedData { get => Get("trained-data", ""); set => Set("trained-data", value); }

    // ---- Print Screen integration ----
    /// <summary>Claim the Print Screen key and stay resident in the notification area.</summary>
    public bool PrintScreenHotkey { get => Get("printscreen-hotkey", false); set => Set("printscreen-hotkey", value); }

    // ---- Window ----
    public bool SidebarVisible { get => Get("sidebar-visible", true); set => Set("sidebar-visible", value); }

    public const string DefaultSnippet = """
        // Gradiwin
        #include <pthread.h>
        #include <stdio.h>

        void* say_hello(void* arg) {
         puts("Hello from thread!");
         return NULL;
        }

        int main() {
         pthread_t t;
         pthread_create(&t, NULL, say_hello, NULL);
         pthread_join(t, NULL);
        }
        """;
}
