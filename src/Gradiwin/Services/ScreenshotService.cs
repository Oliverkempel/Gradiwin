// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core;
using Gradiwin.Views;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;

namespace Gradiwin.Services;

/// <summary>
/// Replaces the xdg screenshot portal used by Gradia's ScreenshotImageLoader:
/// FULL captures every monitor, INTERACTIVE shows a frozen overlay to pick a region or window.
/// Screenshots are saved into the screenshots folder (like Win+PrtScn), then opened.
/// </summary>
public static class ScreenshotService
{
    /// <summary>Screenshots taken during this session (for "trash on close" / Ctrl+Shift+D).</summary>
    public static List<string> TakenScreenshots { get; } = [];

    public static async Task<string?> CaptureAsync(ScreenshotMode mode, int delayMs, Window? hideFrom)
    {
        var hidden = new List<Window>();
        foreach (var w in App.Current.Windows.Where(w => w is MainWindow or PinWindow))
        {
            if (w.AppWindow.IsVisible)
            {
                w.AppWindow.Hide();
                hidden.Add(w);
            }
        }

        try
        {
            // Give the compositor time to remove our windows (Gradia waits 150 ms).
            await Task.Delay(Math.Max(150, delayMs));
            var frame = ScreenCapture.CaptureVirtualScreen();
            var windows = ScreenCapture.EnumerateWindows([]);

            ScreenCapture.Frame? result = frame;
            if (mode == ScreenshotMode.Interactive)
            {
                var picker = new RegionCaptureWindow(frame, windows);
                App.Current.Track(picker);
                result = await picker.PickAsync();
            }
            if (result is null)
                return null;

            return await SaveAsync(result);
        }
        finally
        {
            foreach (var w in hidden)
            {
                if (!ReferenceEquals(w, hideFrom))
                    w.AppWindow.Show();
            }
            if (hideFrom != null)
                hideFrom.AppWindow.Show();
        }
    }

    private static async Task<string> SaveAsync(ScreenCapture.Frame frame)
    {
        var folder = RecentScreenshots.ResolveFolder();
        if (!Directory.Exists(folder))
            folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
        int n = 1;
        while (File.Exists(path))
            path = Path.Combine(folder, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss} ({n++}).png");

        var device = CanvasDevice.GetSharedDevice();
        using var bmp = CanvasBitmap.CreateFromBytes(device, frame.Bgra, frame.Width, frame.Height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        await bmp.SaveAsync(path, CanvasBitmapFileFormat.Png);
        TakenScreenshots.Add(path);
        return path;
    }
}
