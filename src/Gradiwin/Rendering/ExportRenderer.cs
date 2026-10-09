// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;

namespace Gradiwin.Rendering;

/// <summary>
/// Port of BaseImageExporter.get_processed_pixbuf: full-resolution processing,
/// annotations scaled from preview space, then the normalized crop.
/// </summary>
public static class ExportRenderer
{
    public static CanvasRenderTarget Render(LoadedImage image, ImageOptions options, BackgroundSpec background,
        IReadOnlyList<Annotation> annotations, CropRect crop, int previewProcessedWidth, int previewProcessedHeight)
    {
        using var full = ImageProcessor.Process(image, options, background, fullResolution: true);
        var device = full.Device;
        int w = (int)full.SizeInPixels.Width, h = (int)full.SizeInPixels.Height;

        var composed = new CanvasRenderTarget(device, w, h, 96);
        using (var ds = composed.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.DrawImage(full);
            if (annotations.Count > 0 && previewProcessedWidth > 0)
            {
                float scale = (float)((w / (double)previewProcessedWidth + h / (double)previewProcessedHeight) / 2);
                var t = new AnnotationTransform(Vector2.Zero, previewProcessedWidth, previewProcessedHeight, scale);
                ds.Antialiasing = CanvasAntialiasing.Antialiased;
                AnnotationRenderer.Draw(ds, annotations, t, full, scale);
            }
        }

        if (crop.IsFull)
            return composed;

        var (cx, cy, cw, ch) = crop.ToPixels(w, h);
        var cropped = new CanvasRenderTarget(device, cw, ch, 96);
        using (var ds = cropped.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.DrawImage(composed, -cx, -cy);
        }
        composed.Dispose();
        return cropped;
    }

    /// <summary>Renders the cropped preview (used for OCR and the image size label).</summary>
    public static CanvasRenderTarget RenderPreview(CanvasBitmap processedPreview, IReadOnlyList<Annotation> annotations, CropRect crop, bool includeAnnotations)
    {
        var device = processedPreview.Device;
        int w = (int)processedPreview.SizeInPixels.Width, h = (int)processedPreview.SizeInPixels.Height;
        var (cx, cy, cw, ch) = crop.IsFull ? (0, 0, w, h) : crop.ToPixels(w, h);
        var target = new CanvasRenderTarget(device, cw, ch, 96);
        using var ds = target.CreateDrawingSession();
        ds.Clear(Colors.Transparent);
        ds.Transform = Matrix3x2.CreateTranslation(-cx, -cy);
        ds.DrawImage(processedPreview);
        if (includeAnnotations)
            AnnotationRenderer.Draw(ds, annotations, new AnnotationTransform(Vector2.Zero, w, h, 1f), processedPreview, 1f);
        return target;
    }
}
