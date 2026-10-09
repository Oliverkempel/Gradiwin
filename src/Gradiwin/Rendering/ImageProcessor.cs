// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;

namespace Gradiwin.Rendering;

/// <summary>
/// Port of graphics/image_processor.py using Win2D:
/// rotate → auto-balance → negative-padding crop → rounded corners → final size/aspect →
/// background → shadow → composite.
/// </summary>
public static class ImageProcessor
{
    public static ProcessLayout ComputeLayout(LoadedImage image, ImageOptions options, bool fullResolution)
    {
        var src = fullResolution ? image.Full : image.Preview;
        return Layout.Compute((int)src.SizeInPixels.Width, (int)src.SizeInPixels.Height, options, image.Balance,
            fullResolution ? image.FullScale : 1.0);
    }

    /// <summary>Renders the processed image (without annotations).</summary>
    public static CanvasRenderTarget Process(LoadedImage image, ImageOptions options, BackgroundSpec background, bool fullResolution)
    {
        var device = image.Full.Device;
        var src = fullResolution ? image.Full : image.Preview;
        var layout = ComputeLayout(image, options, fullResolution);

        using var content = BuildContent(device, src, options, layout);

        var target = new CanvasRenderTarget(device, layout.FinalWidth, layout.FinalHeight, 96);
        using var ds = target.CreateDrawingSession();
        ds.Clear(Colors.Transparent);
        BackgroundRenderer.Draw(ds, background, layout.FinalWidth, layout.FinalHeight);

        var paste = new Vector2(layout.PasteX, layout.PasteY);

        if (layout.ShadowAlpha > 0)
        {
            var shadow = new ShadowEffect
            {
                Source = content,
                BlurAmount = (float)layout.ShadowBlurRadius,
                ShadowColor = Windows.UI.Color.FromArgb((byte)(layout.ShadowAlpha * 255), 0, 0, 0),
                Optimization = EffectOptimization.Quality,
            };
            ds.DrawImage(shadow, paste);
        }
        ds.DrawImage(content, paste);
        return target;
    }

    /// <summary>The source image after rotation, balance, crop and rounded corners.</summary>
    private static CanvasCommandList BuildContent(CanvasDevice device, CanvasBitmap src, ImageOptions options, ProcessLayout layout)
    {
        int rw = layout.RotatedWidth, rh = layout.RotatedHeight;
        int sw = layout.SourceWidth, sh = layout.SourceHeight;

        // PIL ROTATE_90/180/270 are counter-clockwise.
        var rotation = options.Rotation switch
        {
            90 => Matrix3x2.CreateRotation(-MathF.PI / 2) * Matrix3x2.CreateTranslation(0, sw),
            180 => Matrix3x2.CreateRotation(MathF.PI) * Matrix3x2.CreateTranslation(sw, sh),
            270 => Matrix3x2.CreateRotation(MathF.PI / 2) * Matrix3x2.CreateTranslation(sh, 0),
            _ => Matrix3x2.Identity,
        };

        var offset = Vector2.Zero;
        if (layout.Balance is { } b)
            offset = new Vector2(b.Left, b.Top);
        if (layout.NegativeCrop is { } crop)
            offset -= new Vector2(crop.X, crop.Y);

        var list = new CanvasCommandList(device);
        using (var ds = list.CreateDrawingSession())
        {
            var area = new Windows.Foundation.Rect(0, 0, layout.ContentWidth, layout.ContentHeight);
            CanvasGeometry clip = layout.CornerRadiusPixels > 0
                ? CanvasGeometry.CreateRoundedRectangle(device, area, layout.CornerRadiusPixels, layout.CornerRadiusPixels)
                : CanvasGeometry.CreateRectangle(device, area);
            using (clip)
            using (ds.CreateLayer(1f, clip))
            {
                if (layout.Balance is { } bal)
                {
                    // Balanced canvas is filled with the border color; the image is pasted with its alpha.
                    var balanceArea = new Windows.Foundation.Rect(offset.X - bal.Left, offset.Y - bal.Top,
                        rw + bal.Left + bal.Right, rh + bal.Top + bal.Bottom);
                    ds.FillRectangle(balanceArea, ColorUtil.ToColor(bal.Color));
                }
                ds.Transform = rotation * Matrix3x2.CreateTranslation(offset);
                ds.DrawImage(src, 0, 0, new Windows.Foundation.Rect(0, 0, sw, sh), 1f, CanvasImageInterpolation.NearestNeighbor);
            }
        }
        return list;
    }
}
