// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Gradiwin.Views;

/// <summary>
/// Interactive screenshot picker: a borderless, topmost window covering the whole virtual desktop
/// that shows the frozen capture. Drag to select a region, click to take the window under the
/// cursor, Enter for everything, Esc/right-click to cancel.
/// </summary>
public sealed class RegionCaptureWindow : Window
{
    private readonly ScreenCapture.Frame _frame;
    private readonly List<ScreenCapture.WindowInfo> _windows;
    private readonly CanvasControl _canvas;
    private readonly TaskCompletionSource<ScreenCapture.Frame?> _result = new();
    private readonly Grid _root;
    private CanvasBitmap? _bitmap;
    private Point? _dragStart;
    private Point _cursor;
    private bool _completed;

    public RegionCaptureWindow(ScreenCapture.Frame frame, List<ScreenCapture.WindowInfo> windows)
    {
        _frame = frame;
        _windows = windows;
        Title = Localizer.T("Take a Screenshot");

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;
        int notRounded = 1; // DWMWCP_DONOTROUND
        ScreenCapture.DwmSetWindowAttribute(WindowHelper.GetHwnd(this), 33, ref notRounded, sizeof(int));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(frame.X, frame.Y, frame.Width, frame.Height));

        _canvas = new CanvasControl { ClearColor = Microsoft.UI.Colors.Black };
        _canvas.CreateResources += (s, _) =>
        {
            _bitmap = CanvasBitmap.CreateFromBytes(s, _frame.Bgra, _frame.Width, _frame.Height,
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        };
        _canvas.Draw += OnDraw;
        _canvas.PointerPressed += OnPressed;
        _canvas.PointerMoved += OnMoved;
        _canvas.PointerReleased += OnReleased;

        _root = new Grid { Children = { _canvas } };
        _root.KeyDown += OnKeyDown;
        _root.Loaded += (_, _) =>
        {
            _root.Focus(FocusState.Programmatic);
            SetCursor();
        };
        Content = _root;
        Closed += (_, _) => Complete(null);
        Activated += (_, _) => _root.Focus(FocusState.Programmatic);
    }

    public Task<ScreenCapture.Frame?> PickAsync()
    {
        Activate();
        WindowHelper.BringToFront(this);
        return _result.Task;
    }

    private void SetCursor()
    {
        var cursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_root, cursor);
    }

    private double Scale => _root.XamlRoot?.RasterizationScale ?? 1.0;

    /// <summary>View DIP → physical pixel offset inside the frame.</summary>
    private (int X, int Y) ToPixel(Point p) => ((int)Math.Round(p.X * Scale), (int)Math.Round(p.Y * Scale));

    private Rect ToDip(int x, int y, int w, int h) =>
        new((x - _frame.X) / Scale, (y - _frame.Y) / Scale, w / Scale, h / Scale);

    private ScreenCapture.WindowInfo? WindowAt(Point p)
    {
        var (px, py) = ToPixel(p);
        int sx = px + _frame.X, sy = py + _frame.Y;
        foreach (var w in _windows)
        {
            if (sx >= w.X && sx < w.X + w.Width && sy >= w.Y && sy < w.Y + w.Height)
                return w;
        }
        return null;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(_canvas);
        if (pt.Properties.IsRightButtonPressed)
        {
            Complete(null);
            return;
        }
        _dragStart = pt.Position;
        _canvas.CapturePointer(e.Pointer);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        _cursor = e.GetCurrentPoint(_canvas).Position;
        _canvas.Invalidate();
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is not { } start) return;
        _canvas.ReleasePointerCapture(e.Pointer);
        var end = e.GetCurrentPoint(_canvas).Position;
        _dragStart = null;

        if (Math.Abs(end.X - start.X) < 4 && Math.Abs(end.Y - start.Y) < 4)
        {
            // Click: the window under the cursor, or the monitor if none.
            if (WindowAt(end) is { } w)
            {
                CropAndComplete(w.X - _frame.X, w.Y - _frame.Y, w.Width, w.Height);
            }
            else
            {
                var (px, py) = ToPixel(end);
                foreach (var m in ScreenCapture.Monitors())
                {
                    if (px + _frame.X >= m.X && px + _frame.X < m.X + m.W && py + _frame.Y >= m.Y && py + _frame.Y < m.Y + m.H)
                    {
                        CropAndComplete(m.X - _frame.X, m.Y - _frame.Y, m.W, m.H);
                        return;
                    }
                }
                Complete(_frame);
            }
            return;
        }

        var (x1, y1) = ToPixel(start);
        var (x2, y2) = ToPixel(end);
        CropAndComplete(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
            Complete(null);
        else if (e.Key == Windows.System.VirtualKey.Enter)
            Complete(_frame);
    }

    private void CropAndComplete(int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, _frame.Width - 1);
        y = Math.Clamp(y, 0, _frame.Height - 1);
        w = Math.Clamp(w, 1, _frame.Width - x);
        h = Math.Clamp(h, 1, _frame.Height - y);
        var pixels = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(_frame.Bgra, ((y + row) * _frame.Width + x) * 4, pixels, row * w * 4, w * 4);
        Complete(new ScreenCapture.Frame(_frame.X + x, _frame.Y + y, w, h, pixels));
    }

    private void Complete(ScreenCapture.Frame? frame)
    {
        if (_completed) return;
        _completed = true;
        _result.TrySetResult(frame);
        _canvas.RemoveFromVisualTree();
        Close();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        if (_bitmap is null) return;
        var full = new Rect(0, 0, _frame.Width / Scale, _frame.Height / Scale);
        ds.DrawImage(_bitmap, full, new Rect(0, 0, _frame.Width, _frame.Height), 1f, CanvasImageInterpolation.NearestNeighbor);
        var dim = Windows.UI.Color.FromArgb(110, 0, 0, 0);

        Rect? highlight = null;
        if (_dragStart is { } s)
        {
            highlight = new Rect(new Point(Math.Min(s.X, _cursor.X), Math.Min(s.Y, _cursor.Y)),
                new Point(Math.Max(s.X, _cursor.X), Math.Max(s.Y, _cursor.Y)));
        }
        else if (WindowAt(_cursor) is { } w)
        {
            highlight = ToDip(w.X, w.Y, w.Width, w.Height);
        }

        if (highlight is { } h)
        {
            using var outside = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateRectangle(ds, full);
            using var inside = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateRectangle(ds, h);
            using var mask = outside.CombineWith(inside, Matrix3x2.Identity, Microsoft.Graphics.Canvas.Geometry.CanvasGeometryCombine.Exclude);
            ds.FillGeometry(mask, dim);
            var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight1"];
            ds.DrawRectangle(h, accent, 2);

            int pw = (int)Math.Round(h.Width * Scale), ph = (int)Math.Round(h.Height * Scale);
            using var format = new CanvasTextFormat { FontSize = 13, FontFamily = "Segoe UI Variable Text" };
            var label = $"{pw} × {ph}";
            using var layout = new CanvasTextLayout(ds, label, format, 400, 40);
            var lb = layout.LayoutBounds;
            float lx = (float)h.X, ly = (float)(h.Y - lb.Height - 10);
            if (ly < 4) ly = (float)h.Y + 6;
            ds.FillRoundedRectangle(lx, ly, (float)lb.Width + 12, (float)lb.Height + 6, 4, 4, Windows.UI.Color.FromArgb(200, 32, 32, 32));
            ds.DrawTextLayout(layout, lx + 6, ly + 3, Microsoft.UI.Colors.White);
        }
        else
        {
            ds.FillRectangle(full, dim);
        }

        // Hint text.
        using var hintFormat = new CanvasTextFormat { FontSize = 15, FontFamily = "Segoe UI Variable Text", HorizontalAlignment = CanvasHorizontalAlignment.Center };
        var hint = Localizer.T("Drag to select an area, click a window, Enter for all screens, Esc to cancel");
        ds.FillRoundedRectangle((float)(full.Width / 2 - 330), 24, 660, 34, 8, 8, Windows.UI.Color.FromArgb(200, 32, 32, 32));
        ds.DrawText(hint, new Rect(full.Width / 2 - 330, 30, 660, 30), Microsoft.UI.Colors.White, hintFormat);
    }
}
