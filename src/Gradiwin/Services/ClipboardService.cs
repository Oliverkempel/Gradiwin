// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Graphics.Canvas;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Gradiwin.Services;

/// <summary>Port of gradia/clipboard.py, plus reading raw bitmaps (Gradia only reads file lists).</summary>
public static class ClipboardService
{
    private static readonly string[] s_imageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".avif", ".bmp", ".gif", ".tif", ".tiff"];

    public static bool IsImageFile(string path) => s_imageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Writes the image to the clipboard as PNG (with alpha) and as a bitmap for older apps.</summary>
    public static async Task CopyImageAsync(CanvasBitmap image, string tempFolder)
    {
        var png = await ImageEncoder.EncodePngAsync(image);
        var path = Path.Combine(tempFolder, "clipboard.png");
        await File.WriteAllBytesAsync(path, png);

        var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(png.AsBuffer());
        ras.Seek(0);

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(ras));
        package.SetData("PNG", RandomAccessStreamReference.CreateFromStream(ras.CloneStream()));
        var file = await StorageFile.GetFileFromPathAsync(path);
        package.SetStorageItems([file], readOnly: true);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    /// <summary>Returns an image file path from the clipboard: a copied file, or a bitmap saved into <paramref name="tempFolder"/>.</summary>
    public static async Task<(string Path, bool FromFile)?> ReadImageAsync(string tempFolder)
    {
        var content = Clipboard.GetContent();

        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var items = await content.GetStorageItemsAsync();
            var file = items.OfType<StorageFile>().FirstOrDefault(f => IsImageFile(f.Path));
            if (file != null)
                return (file.Path, true);
        }

        if (content.Contains("PNG"))
        {
            if (await content.GetDataAsync("PNG") is IRandomAccessStream pngStream)
                return (await SaveStreamAsync(pngStream.AsStreamForRead(), tempFolder), false);
        }

        if (content.Contains(StandardDataFormats.Bitmap))
        {
            var reference = await content.GetBitmapAsync();
            using var stream = await reference.OpenReadAsync();
            var device = CanvasDevice.GetSharedDevice();
            using var bitmap = await CanvasBitmap.LoadAsync(device, stream);
            var path = Path.Combine(tempFolder, $"clipboard-{DateTime.Now:HHmmss-fff}.png");
            await bitmap.SaveAsync(path, CanvasBitmapFileFormat.Png);
            return (path, false);
        }

        if (content.Contains(StandardDataFormats.Text))
        {
            var text = (await content.GetTextAsync()).Trim().Trim('"');
            if (File.Exists(text) && IsImageFile(text))
                return (text, true);
        }
        return null;
    }

    private static async Task<string> SaveStreamAsync(Stream stream, string folder)
    {
        var path = Path.Combine(folder, $"clipboard-{DateTime.Now:HHmmss-fff}.png");
        await using var fs = File.Create(path);
        await stream.CopyToAsync(fs);
        return path;
    }
}
