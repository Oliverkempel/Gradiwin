// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Gradiwin.Services;

public sealed record ExportFormat(string Key, string Name, string ShortName, string MimeType, string[] Extensions, bool HasQuality);

/// <summary>Encodes final images (port of SUPPORTED_EXPORT_FORMATS + FileDialogExporter._save_image_pixbuf).</summary>
public static class ImageEncoder
{
    public const float Quality = 0.9f;

    public static readonly IReadOnlyList<ExportFormat> Formats =
    [
        new("png", "PNG Image (*.png)", "PNG", "image/png", [".png"], false),
        new("jpeg", "JPEG Image (*.jpg)", "JPEG", "image/jpeg", [".jpg", ".jpeg"], true),
        new("webp", "WebP Image (*.webp)", "WebP", "image/webp", [".webp"], true),
    ];

    public const string DefaultFormat = "jpeg";

    public static ExportFormat Get(string? key) =>
        Formats.FirstOrDefault(f => f.Key == key) ?? Formats.First(f => f.Key == DefaultFormat);

    public static ExportFormat? FromExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return Formats.FirstOrDefault(f => f.Extensions.Contains(ext));
    }

    public static async Task SaveAsync(CanvasBitmap image, string path, ExportFormat format, bool compress)
    {
        using var stream = File.Create(path + ".tmp");
        await EncodeAsync(image, stream, format, compress);
        stream.Close();
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static async Task EncodeAsync(CanvasBitmap image, Stream output, ExportFormat format, bool compress)
    {
        switch (format.Key)
        {
            case "jpeg":
            {
                // JPEG has no alpha: flatten onto white like _convert_rgba_to_rgb.
                using var flat = Flatten(image, Microsoft.UI.Colors.White);
                await EncodeWicAsync(flat, output, BitmapEncoder.JpegEncoderId, compress ? Quality : 1.0f);
                break;
            }
            case "webp":
                EncodeWebp(image, output, compress);
                break;
            default:
                await EncodeWicAsync(image, output, BitmapEncoder.PngEncoderId, null);
                break;
        }
    }

    public static async Task<byte[]> EncodePngAsync(CanvasBitmap image)
    {
        using var ms = new MemoryStream();
        await EncodeWicAsync(image, ms, BitmapEncoder.PngEncoderId, null);
        return ms.ToArray();
    }

    private static CanvasRenderTarget Flatten(CanvasBitmap image, Windows.UI.Color background)
    {
        var size = image.SizeInPixels;
        var rt = new CanvasRenderTarget(image.Device, size.Width, size.Height, 96);
        using var ds = rt.CreateDrawingSession();
        ds.Clear(background);
        ds.DrawImage(image);
        return rt;
    }

    private static async Task EncodeWicAsync(CanvasBitmap image, Stream output, Guid encoderId, float? quality)
    {
        var size = image.SizeInPixels;
        var pixels = image.GetPixelBytes();
        using var ras = new InMemoryRandomAccessStream();
        BitmapPropertySet? props = null;
        if (quality is { } q)
            props = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(q, Windows.Foundation.PropertyType.Single) };
        var encoder = props != null
            ? await BitmapEncoder.CreateAsync(encoderId, ras, props)
            : await BitmapEncoder.CreateAsync(encoderId, ras);
        // Win2D bitmaps are premultiplied; WIC expects straight alpha for PNG.
        Unpremultiply(pixels);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, size.Width, size.Height, 96, 96, pixels);
        await encoder.FlushAsync();
        ras.Seek(0);
        await ras.AsStreamForRead().CopyToAsync(output);
    }

    private static void EncodeWebp(CanvasBitmap image, Stream output, bool compress)
    {
        var size = image.SizeInPixels;
        var pixels = image.GetPixelBytes();
        var info = new SkiaSharp.SKImageInfo((int)size.Width, (int)size.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var pixmap = new SkiaSharp.SKPixmap(info, handle.AddrOfPinnedObject(), info.RowBytes);
            var options = compress
                ? new SkiaSharp.SKWebpEncoderOptions(SkiaSharp.SKWebpEncoderCompression.Lossy, Quality * 100)
                : new SkiaSharp.SKWebpEncoderOptions(SkiaSharp.SKWebpEncoderCompression.Lossless, 100);
            using var data = pixmap.Encode(options) ?? throw new InvalidOperationException("WebP encoding failed");
            data.SaveTo(output);
        }
        finally
        {
            handle.Free();
        }
    }

    public static void Unpremultiply(byte[] bgra)
    {
        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (a == 255 || a == 0) continue;
            bgra[i] = (byte)Math.Min(255, bgra[i] * 255 / a);
            bgra[i + 1] = (byte)Math.Min(255, bgra[i + 1] * 255 / a);
            bgra[i + 2] = (byte)Math.Min(255, bgra[i + 2] * 255 / a);
        }
    }
}
