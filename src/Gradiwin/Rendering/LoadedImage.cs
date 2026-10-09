// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Models;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas;

namespace Gradiwin.Rendering;

/// <summary>Port of graphics/loaded_image.py: full-resolution bitmap plus a ≤1 MP preview.</summary>
public sealed class LoadedImage : IDisposable
{
    private LoadedImage(string path, ImageOrigin origin, CanvasBitmap full, CanvasBitmap preview, BalancedPadding balance)
    {
        ImagePath = path;
        Origin = origin;
        Full = full;
        Preview = preview;
        Balance = balance;
    }

    public string ImagePath { get; }
    public ImageOrigin Origin { get; }
    public CanvasBitmap Full { get; }
    public CanvasBitmap Preview { get; }
    public BalancedPadding Balance { get; }

    /// <summary>Original screenshot file (for "overwrite screenshot" / trash), if any.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>Copy the result to the clipboard as soon as it is processed (screenshot flow).</summary>
    public bool CopyAfterProcessing { get; set; }

    public int FullWidth => (int)Full.SizeInPixels.Width;
    public int FullHeight => (int)Full.SizeInPixels.Height;
    public int PreviewWidth => (int)Preview.SizeInPixels.Width;
    public int PreviewHeight => (int)Preview.SizeInPixels.Height;

    /// <summary>Full/preview scale (Gradia uses the max of both axes).</summary>
    public double FullScale => Math.Max(FullWidth / (double)PreviewWidth, FullHeight / (double)PreviewHeight);

    public bool IsScreenshot => Origin is ImageOrigin.Screenshot or ImageOrigin.FakeScreenshot;

    public bool HasProperName => Origin is not (ImageOrigin.Clipboard or ImageOrigin.Screenshot or ImageOrigin.FakeScreenshot or ImageOrigin.SourceImage);

    public string ProperName(bool withExtension = true) => Origin switch
    {
        ImageOrigin.Clipboard => Localizer.T("Clipboard Image"),
        ImageOrigin.Screenshot or ImageOrigin.FakeScreenshot => Localizer.T("Screenshot"),
        ImageOrigin.SourceImage => Localizer.T("Generated Image"),
        _ => withExtension ? Path.GetFileName(ImagePath) : Path.GetFileNameWithoutExtension(ImagePath),
    };

    public string ProperFolder => Origin switch
    {
        ImageOrigin.Clipboard => Localizer.T("From clipboard"),
        ImageOrigin.Screenshot or ImageOrigin.FakeScreenshot => Localizer.T("Screenshot"),
        ImageOrigin.SourceImage => Localizer.T("Source"),
        _ => Path.GetFileName(Path.GetDirectoryName(ImagePath)) ?? "",
    };

    public string? FolderPath => Path.GetDirectoryName(ImagePath);

    public static async Task<LoadedImage> LoadAsync(string path, ImageOrigin origin, string? screenshotPath = null)
    {
        var device = CanvasDevice.GetSharedDevice();
        CanvasBitmap full;
        try
        {
            full = await CanvasBitmap.LoadAsync(device, path, 96);
        }
        catch (Exception e) when (e.HResult == unchecked((int)0x88982F50) || e.HResult == unchecked((int)0x88982F07))
        {
            // WINCODEC_ERR_COMPONENTNOTFOUND / WINCODEC_ERR_UNKNOWNIMAGEFORMAT
            throw new NotSupportedException(Path.GetExtension(path).Equals(".avif", StringComparison.OrdinalIgnoreCase)
                ? Localizer.T("AVIF images need the AV1 Video Extension from the Microsoft Store.")
                : Localizer.T("Unsupported image format."), e);
        }

        return FromBitmap(full, path, origin, screenshotPath);
    }

    public static LoadedImage FromBitmap(CanvasBitmap full, string path, ImageOrigin origin, string? screenshotPath = null)
    {
        var device = full.Device;
        int w = (int)full.SizeInPixels.Width, h = (int)full.SizeInPixels.Height;
        var (pw, ph) = Layout.PreviewSize(w, h);

        CanvasBitmap preview;
        if (pw == w && ph == h)
        {
            preview = full;
        }
        else
        {
            var rt = new CanvasRenderTarget(device, pw, ph, 96);
            using (var ds = rt.CreateDrawingSession())
            {
                ds.Clear(Microsoft.UI.Colors.Transparent);
                ds.DrawImage(full, new Windows.Foundation.Rect(0, 0, pw, ph), new Windows.Foundation.Rect(0, 0, w, h), 1f,
                    CanvasImageInterpolation.HighQualityCubic);
            }
            preview = rt;
        }

        var pixels = preview.GetPixelBytes();
        var balance = PaddingAnalyzer.Analyze(pixels, pw, ph);
        return new LoadedImage(path, origin, full, preview, balance) { ScreenshotPath = screenshotPath };
    }

    public void Dispose()
    {
        if (!ReferenceEquals(Preview, Full))
            Preview.Dispose();
        Full.Dispose();
    }
}
