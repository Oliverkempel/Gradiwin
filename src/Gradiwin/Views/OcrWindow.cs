// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gradiwin.Views;

/// <summary>Port of ui/dialog/ocr_launcher.py: standalone OCR for --ocr-file; copies the text automatically.</summary>
public sealed class OcrWindow : Window
{
    public OcrWindow(string path, bool standalone)
    {
        Title = Localizer.T("Text Extraction");
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        WindowHelper.SetIcon(this);
        WindowHelper.Resize(this, 560, 420);
        var root = new Grid();
        Content = root;
        root.Loaded += async (_, _) =>
        {
            try
            {
                using var bitmap = await Rendering.LoadedImage.LoadUnlockedAsync(CanvasDevice.GetSharedDevice(), Path.GetFullPath(path));
                await OcrDialog.ShowAsync(root.XamlRoot, bitmap, copyAutomatically: standalone);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException || e.HResult != 0)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = root.XamlRoot,
                    Title = Localizer.T("Failed to load image"),
                    Content = e.Message,
                    CloseButtonText = Localizer.T("Close"),
                };
                await dialog.ShowAsync();
            }
            Close();
        };
    }
}
