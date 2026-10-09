// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Graphics.Canvas;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Gradiwin.Services;

/// <summary>
/// Replaces Gradia's tesseract backend (backend/ocr.py) with the built-in Windows OCR engine.
/// Languages come from installed Windows language packs instead of downloaded tessdata models.
/// </summary>
public static class OcrService
{
    public static IReadOnlyList<Language> AvailableLanguages => OcrEngine.AvailableRecognizerLanguages;

    public static Language? SelectedLanguage
    {
        get
        {
            var tag = SettingsService.Instance.TrainedData;
            if (string.IsNullOrEmpty(tag) || tag == "eng") return null;
            return AvailableLanguages.FirstOrDefault(l => l.LanguageTag.Equals(tag, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static async Task<string> RecognizeAsync(CanvasBitmap bitmap, Language? language = null)
    {
        language ??= SelectedLanguage;
        var engine = language != null && OcrEngine.IsLanguageSupported(language)
            ? OcrEngine.TryCreateFromLanguage(language)
            : OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
            throw new InvalidOperationException(Localizer.T("No OCR language is installed. Add a language with OCR support in Windows Settings."));

        // Windows OCR has a maximum image dimension; downscale large images.
        var size = bitmap.SizeInPixels;
        uint max = OcrEngine.MaxImageDimension;
        CanvasBitmap source = bitmap;
        CanvasRenderTarget? scaled = null;
        if (size.Width > max || size.Height > max)
        {
            double f = Math.Min(max / (double)size.Width, max / (double)size.Height);
            scaled = new CanvasRenderTarget(bitmap.Device, (float)(size.Width * f), (float)(size.Height * f), 96);
            using (var ds = scaled.CreateDrawingSession())
                ds.DrawImage(bitmap, new Windows.Foundation.Rect(0, 0, size.Width * f, size.Height * f));
            source = scaled;
        }

        try
        {
            var sz = source.SizeInPixels;
            using var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(source.GetPixelBytes().AsBuffer(),
                BitmapPixelFormat.Bgra8, (int)sz.Width, (int)sz.Height, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(softwareBitmap);
            var sb = new StringBuilder();
            foreach (var line in result.Lines)
                sb.AppendLine(line.Text);
            return sb.ToString().TrimEnd();
        }
        finally
        {
            scaled?.Dispose();
        }
    }
}
