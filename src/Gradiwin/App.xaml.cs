// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;
using Gradiwin.Rendering;
using Gradiwin.Services;
using Gradiwin.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;

namespace Gradiwin;

/// <summary>Port of main.py GradiaApp: routes every activation (launch, files, share, CLI) to windows.</summary>
public partial class App : Application
{
    private readonly List<Window> _windows = [];
    private DispatcherQueue? _dispatcher;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    public static new App Current => (App)Application.Current;

    public IReadOnlyList<Window> Windows => _windows;

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Log.Info($"Launched; resources={Resources.Count}, merged={Resources.MergedDictionaries.Count}, keys={string.Join(",", Resources.Keys.Take(20))}");
        TextAnnotation.Measurer = new AnnotationRenderer.Win2DTextMeasurer();
        SessionFiles.CleanupStale();
        NotificationService.Initialize();
        NotificationService.Invoked += a =>
        {
            if (a.TryGetValue("action", out var action) && action == "open-folder" && a.TryGetValue("path", out var path))
                WindowHelper.OpenFolder(path);
        };

        AppInstance.GetCurrent().Activated += (_, e) => _dispatcher.TryEnqueue(() => _ = HandleActivationAsync(e, isFirst: false));
        _ = HandleActivationAsync(AppInstance.GetCurrent().GetActivatedEventArgs(), isFirst: true);
    }

    private async Task HandleActivationAsync(AppActivationArguments activation, bool isFirst)
    {
        try
        {
            await HandleActivationCoreAsync(activation, isFirst);
        }
        catch (Exception e)
        {
            Log.Error("Activation failed", e);
            if (_windows.Count == 0)
                OpenWindow();
        }
    }

    private async Task HandleActivationCoreAsync(AppActivationArguments activation, bool isFirst)
    {
        Log.Info($"Activation: {activation.Kind}");
        CommandLineOptions options;
        switch (activation.Kind)
        {
            case ExtendedActivationKind.File when activation.Data is IFileActivatedEventArgs fileArgs:
                options = new CommandLineOptions { Files = fileArgs.Files.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList() };
                break;

            case ExtendedActivationKind.ShareTarget when activation.Data is IShareTargetActivatedEventArgs shareArgs:
                options = await ReceiveShareAsync(shareArgs.ShareOperation);
                break;

            case ExtendedActivationKind.AppNotification:
                if (_windows.Count > 0)
                {
                    WindowHelper.BringToFront(_windows[^1]);
                    return;
                }
                options = new CommandLineOptions();
                break;

            case ExtendedActivationKind.Launch:
            default:
                IReadOnlyList<string> args;
                if (isFirst)
                {
                    args = Environment.GetCommandLineArgs().Skip(1).ToList();
#if DEBUG
                    if (args.Count == 3 && args[0] == "--selftest")
                    {
                        var report = await Diagnostics.SelfTest.RunAsync(args[1], args[2]);
                        Log.Info("Self-test report: " + report);
                        Exit();
                        return;
                    }
                    if (args.Count == 1 && args[0].StartsWith("--debug-window=", StringComparison.Ordinal))
                    {
                        var main = OpenWindow();
                        switch (args[0]["--debug-window=".Length..])
                        {
                            case "preferences": main.OpenPreferences(); break;
                            case "upload": main.OpenPreferences(PreferencesWindow.Page.Upload); break;
                            case "ocr": main.OpenPreferences(PreferencesWindow.Page.Ocr); break;
                            case "snippet": var sw = new SourceSnippetWindow(_ => Task.CompletedTask); Track(sw); sw.Activate(); break;
                        }
                        return;
                    }
#endif
                }
                else
                {
                    var raw = (activation.Data as ILaunchActivatedEventArgs)?.Arguments ?? "";
                    var split = CommandLineOptions.SplitCommandLine(raw).ToList();
                    if (split.Count > 0 && split[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        split.RemoveAt(0);
                    args = split;
                }
                options = CommandLineOptions.Parse(args);
                break;
        }

        var inbox = StdinImageReader.DrainInbox();
        if (inbox.Count > 0)
            options = options with { Files = [.. options.Files, .. inbox] };

        await ExecuteAsync(options);
    }

    private static async Task<CommandLineOptions> ReceiveShareAsync(ShareOperation share)
    {
        var files = new List<string>();
        try
        {
            share.ReportStarted();
            var data = share.Data;
            var folder = Path.Combine(Path.GetTempPath(), "Gradiwin", "shared");
            Directory.CreateDirectory(folder);
            if (data.Contains(StandardDataFormats.StorageItems))
            {
                foreach (var item in (await data.GetStorageItemsAsync()).OfType<StorageFile>())
                {
                    var copy = await item.CopyAsync(await StorageFolder.GetFolderFromPathAsync(folder), item.Name, NameCollisionOption.GenerateUniqueName);
                    files.Add(copy.Path);
                }
            }
            else if (data.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = await data.GetBitmapAsync();
                using var stream = await bitmap.OpenReadAsync();
                var path = Path.Combine(folder, $"shared-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                await using (var fs = File.Create(path))
                    await stream.AsStreamForRead().CopyToAsync(fs);
                files.Add(path);
            }
            share.ReportCompleted();
        }
        catch (Exception e)
        {
            share.ReportError(e.Message);
        }
        return new CommandLineOptions { Files = files };
    }

    /// <summary>Port of GradiaApp.do_command_line / do_open.</summary>
    public async Task ExecuteAsync(CommandLineOptions options)
    {
        if (options.OcrFile is { } ocrFile)
        {
            var w = new OcrWindow(ocrFile, standalone: true);
            Track(w);
            w.Activate();
            return;
        }

        foreach (var pin in options.PinFiles)
        {
            var w = new PinWindow(pin);
            Track(w);
            w.Activate();
        }

        if (options.Screenshot != ScreenshotMode.None)
        {
            await TakeScreenshotIntoNewWindowAsync(options.Screenshot, options.DelayMs, hideFrom: null);
            return;
        }

        if (options.ScreenshotFile is { } shot)
        {
            var w = OpenWindow();
            await w.LoadFileAsync(shot, ImageOrigin.FakeScreenshot, screenshotPath: shot, copyAfterProcessing: true);
        }

        foreach (var file in options.Files)
        {
            var w = OpenWindow();
            await w.LoadFileAsync(file, ImageOrigin.CommandLine);
        }

        if (options.Files.Count == 0 && options.ScreenshotFile == null && options.PinFiles.Count == 0)
            OpenWindow();
    }

    public MainWindow OpenWindow()
    {
        var w = new MainWindow();
        Track(w);
        w.Activate();
        return w;
    }

    public void Track(Window window)
    {
        _windows.Add(window);
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            if (_windows.Count == 0)
            {
                SettingsService.Instance.Flush();
                NotificationService.Unregister();
            }
        };
    }

    /// <summary>Screenshot flow used by --screenshot and by windows without an image.</summary>
    public async Task TakeScreenshotIntoNewWindowAsync(ScreenshotMode mode, int delayMs, MainWindow? hideFrom)
    {
        var result = await ScreenshotService.CaptureAsync(mode, delayMs, hideFrom);
        if (result is null)
        {
            if (_windows.Count == 0)
                Exit();
            return;
        }
        var target = hideFrom is { HasImage: false } ? hideFrom : OpenWindow();
        await target.LoadFileAsync(result, ImageOrigin.Screenshot, screenshotPath: result, copyAfterProcessing: true);
        WindowHelper.BringToFront(target);
    }
}
