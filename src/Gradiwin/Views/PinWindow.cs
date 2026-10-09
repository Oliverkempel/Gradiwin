// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Gradiwin.Views;

/// <summary>
/// Port of ui/pin_window.py: an always-on-top borderless image window.
/// Drag to move, it fades while hovered so content below stays visible, Esc or double-click closes.
/// </summary>
public sealed class PinWindow : Window
{
    private const double HoverOpacity = 0.35;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Grid _root;
    private bool _dragging;
    private Windows.Graphics.PointInt32 _dragOrigin;
    private Windows.Foundation.Point _dragStart;

    public PinWindow(string path)
    {
        Title = Path.GetFileName(path) + " — " + Localizer.T("Pinned");
        WindowHelper.SetIcon(this);
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, false);
            p.IsAlwaysOnTop = true;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }

        _root = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Children = { _image } };
        var close = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Padding = new Thickness(6),
            Opacity = 0,
        };
        ToolTipService.SetToolTip(close, Localizer.T("Close"));
        close.Click += (_, _) => Close();
        _root.Children.Add(close);
        Content = _root;

        _root.PointerEntered += (_, _) => { _image.Opacity = HoverOpacity; close.Opacity = 1; };
        _root.PointerExited += (_, _) => { if (!_dragging) { _image.Opacity = 1; close.Opacity = 0; } };
        _root.PointerPressed += (_, e) =>
        {
            _dragging = true;
            _dragOrigin = AppWindow.Position;
            _dragStart = ScreenPoint(e);
            _root.CapturePointer(e.Pointer);
        };
        _root.PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            var p = ScreenPoint(e);
            AppWindow.Move(new Windows.Graphics.PointInt32(_dragOrigin.X + (int)(p.X - _dragStart.X), _dragOrigin.Y + (int)(p.Y - _dragStart.Y)));
        };
        _root.PointerReleased += (_, e) =>
        {
            _dragging = false;
            _root.ReleasePointerCapture(e.Pointer);
        };
        _root.DoubleTapped += (_, _) => Close();
        _root.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) Close(); };
        _ = LoadAsync(path);
    }

    private Windows.Foundation.Point ScreenPoint(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var local = e.GetCurrentPoint(_root).Position;
        double scale = _root.XamlRoot?.RasterizationScale ?? 1;
        var pos = AppWindow.Position;
        return new Windows.Foundation.Point(pos.X + local.X * scale, pos.Y + local.Y * scale);
    }

    private async Task LoadAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            _image.Source = bitmap;

            // Size to the image, capped to 60% of the work area.
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            double w = bitmap.PixelWidth, h = bitmap.PixelHeight;
            double f = Math.Min(1, Math.Min(area.Width * 0.6 / w, area.Height * 0.6 / h));
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(w * f), (int)(h * f)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException || e.HResult != 0)
        {
            Close();
        }
    }
}
