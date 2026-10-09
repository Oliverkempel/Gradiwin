// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Gradiwin.Controls;

/// <summary>Port of widget/angle_selector.py: a dial that snaps to 0/90/180/270 within 15°.</summary>
public sealed partial class AngleSelector : UserControl
{
    private const double SnapThreshold = 15;
    private readonly CanvasControl _canvas;
    private double _angle;
    private bool _dragging;

    public event Action<double>? AngleChanged;

    public AngleSelector()
    {
        Width = 44;
        Height = 44;
        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        _canvas.PointerPressed += OnPressed;
        _canvas.PointerMoved += OnMoved;
        _canvas.PointerReleased += (_, e) => { _dragging = false; _canvas.ReleasePointerCapture(e.Pointer); };
        Content = _canvas;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        IsEnabledChanged += (_, _) => _canvas.Invalidate();
        Unloaded += (_, _) => _canvas.RemoveFromVisualTree();
    }

    public double Angle
    {
        get => _angle;
        set
        {
            _angle = ((value % 360) + 360) % 360;
            _canvas.Invalidate();
        }
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) return;
        _dragging = true;
        _canvas.CapturePointer(e.Pointer);
        Update(e);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging) Update(e);
    }

    private void Update(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_canvas).Position;
        double cx = ActualWidth / 2, cy = ActualHeight / 2;
        double deg = Math.Atan2(p.Y - cy, p.X - cx) * 180 / Math.PI;
        deg = ((deg % 360) + 360) % 360;
        foreach (var snap in new[] { 0.0, 90, 180, 270, 360 })
        {
            if (Math.Abs(deg - snap) <= SnapThreshold)
            {
                deg = snap % 360;
                break;
            }
        }
        deg = Math.Round(deg);
        if (deg != _angle)
        {
            Angle = deg;
            AngleChanged?.Invoke(deg);
        }
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        float w = (float)sender.ActualWidth, h = (float)sender.ActualHeight;
        var c = new Vector2(w / 2, h / 2);
        float r = MathF.Min(w, h) / 2 - 2;
        var fg = (Resources.TryGetValue("TextFillColorPrimary", out var v) && v is Windows.UI.Color col)
            ? col
            : ActualTheme == ElementTheme.Light ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        if (!IsEnabled) fg.A = 80;
        var ring = fg;
        ring.A = (byte)(ring.A * 0.35);
        ds.DrawCircle(c, r, ring, 2);
        double rad = _angle * Math.PI / 180;
        var tip = c + new Vector2((float)Math.Cos(rad), (float)Math.Sin(rad)) * (r - 5);
        ds.DrawLine(c, tip, fg, 2.5f, new Microsoft.Graphics.Canvas.Geometry.CanvasStrokeStyle
        {
            StartCap = Microsoft.Graphics.Canvas.Geometry.CanvasCapStyle.Round,
            EndCap = Microsoft.Graphics.Canvas.Geometry.CanvasCapStyle.Round,
        });
        ds.FillCircle(tip, 3.5f, fg);
    }
}
