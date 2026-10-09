// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Models;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;

namespace Gradiwin.Rendering;

/// <summary>What to draw behind the image. Equivalent of Gradia's Background subclasses.</summary>
public sealed class BackgroundSpec
{
    public BackgroundMode Mode { get; init; }
    public SolidBackground Solid { get; init; } = new();
    public Gradient Gradient { get; init; } = new();
    public CanvasBitmap? Image { get; init; }

    public static readonly BackgroundSpec None = new() { Mode = BackgroundMode.None };
}

public static class BackgroundRenderer
{
    // Small cache so slider drags don't regenerate the same gradient.
    private static (string Key, CanvasBitmap Bitmap)? s_gradientCache;

    public static void Draw(CanvasDrawingSession ds, BackgroundSpec spec, int width, int height)
    {
        switch (spec.Mode)
        {
            case BackgroundMode.Solid:
                ds.FillRectangle(0, 0, width, height, ColorUtil.ToColor(spec.Solid.Rgba));
                break;

            case BackgroundMode.Gradient:
                ds.DrawImage(GetGradientBitmap(ds.Device, spec.Gradient, width, height), 0, 0);
                break;

            case BackgroundMode.Image when spec.Image is not null:
                var img = spec.Image;
                int iw = (int)img.SizeInPixels.Width, ih = (int)img.SizeInPixels.Height;
                var (x, y, w, h) = Layout.CoverRect(iw, ih, width, height);
                ds.DrawImage(img, new Rect(x, y, w, h), new Rect(0, 0, iw, ih), 1f, CanvasImageInterpolation.HighQualityCubic);
                break;
        }
    }

    public static CanvasBitmap GetGradientBitmap(ICanvasResourceCreator device, Gradient gradient, int width, int height)
    {
        var key = $"{gradient.ToJson()}|{width}x{height}";
        if (s_gradientCache is { } cached && cached.Key == key && cached.Bitmap.Device == device.Device)
            return cached.Bitmap;

        var pixels = GradientGenerator.GenerateBgra(gradient, width, height);
        var bmp = CanvasBitmap.CreateFromBytes(device, pixels, width, height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        s_gradientCache?.Bitmap.Dispose();
        s_gradientCache = (key, bmp);
        return bmp;
    }
}
