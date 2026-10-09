// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Models;
using Gradiwin.Rendering;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Gradiwin.Controls;

/// <summary>
/// Port of widget/gradient_editor.py: a gradient bar with draggable color stops.
/// Click on empty space adds an interpolated stop, drag moves (stops can't overlap),
/// click selects, double-click (or Enter) edits the color, Delete removes the selected stop.
/// </summary>
public sealed partial class GradientEditor : UserControl
{
    private const float HandleRadius = 13;
    private const double MinClickDistance = 0.13;
    private const int MaxStops = 6;

    private readonly CanvasControl _canvas;
    private List<GradientStop> _stops = Gradient.DefaultSteps();
    private int _selected = -1;
    private int _dragging = -1;
    private double _dragOffset;
    private bool _moved;

    public event Action<IReadOnlyList<GradientStop>>? StopsChanged;

    public GradientEditor()
    {
        Height = 40;
        IsTabStop = true;
        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        _canvas.PointerPressed += OnPressed;
        _canvas.PointerMoved += OnMoved;
        _canvas.PointerReleased += OnReleased;
        _canvas.DoubleTapped += OnDoubleTapped;
        Content = _canvas;
        KeyDown += OnKeyDown;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        ToolTipService.SetToolTip(this, Localizer.T("Click to add a color stop, drag to move, double-click to change its color, Delete to remove it."));
        Unloaded += (_, _) => _canvas.RemoveFromVisualTree();
    }

    public IReadOnlyList<GradientStop> Stops => _stops;

    public int SelectedIndex => _selected;

    public void SetStops(IEnumerable<GradientStop> stops)
    {
        _stops = stops.OrderBy(s => s.Position).ToList();
        if (_selected >= _stops.Count) _selected = -1;
        _canvas.Invalidate();
    }

    public void RemoveSelected()
    {
        if (_selected < 0 || _stops.Count <= 2) return;
        _stops.RemoveAt(_selected);
        _selected = -1;
        Commit();
    }

    private float BarLeft => HandleRadius;
    private float BarWidth => (float)Math.Max(1, ActualWidth - 2 * HandleRadius);

    private double PositionFromX(double x) => Math.Clamp((x - BarLeft) / BarWidth, 0, 1);

    private int HitStop(double x)
    {
        for (int i = _stops.Count - 1; i >= 0; i--)
        {
            double sx = BarLeft + _stops[i].Position * BarWidth;
            if (Math.Abs(sx - x) <= HandleRadius) return i;
        }
        return -1;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var p = e.GetCurrentPoint(_canvas).Position;
        int hit = HitStop(p.X);
        if (hit >= 0)
        {
            _selected = hit;
            _dragging = hit;
            _dragOffset = p.X - (BarLeft + _stops[hit].Position * BarWidth);
            _moved = false;
            _canvas.CapturePointer(e.Pointer);
        }
        else
        {
            double pos = PositionFromX(p.X);
            if (_stops.Count < MaxStops && !_stops.Any(s => Math.Abs(s.Position - pos) < MinClickDistance))
            {
                var color = GradientGenerator.ColorAt(_stops, pos);
                _stops.Add(new GradientStop(Math.Round(pos, 3), color.ToRgbString()));
                _stops = _stops.OrderBy(s => s.Position).ToList();
                _selected = _stops.FindIndex(s => Math.Abs(s.Position - Math.Round(pos, 3)) < 1e-9);
                Commit();
            }
        }
        _canvas.Invalidate();
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging < 0) return;
        var p = e.GetCurrentPoint(_canvas).Position;
        double pos = Math.Round(PositionFromX(p.X - _dragOffset), 3);
        // Prevent overlapping handles (Gradia's _check_overlap_at_step).
        double minGap = 2 * HandleRadius / BarWidth;
        for (int i = 0; i < _stops.Count; i++)
        {
            if (i == _dragging) continue;
            if (Math.Abs(_stops[i].Position - pos) < minGap) return;
        }
        _stops[_dragging] = _stops[_dragging] with { Position = pos };
        _moved = true;
        _canvas.Invalidate();
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging < 0) return;
        _canvas.ReleasePointerCapture(e.Pointer);
        var moved = _stops[_dragging];
        _dragging = -1;
        if (_moved)
        {
            _stops = _stops.OrderBy(s => s.Position).ToList();
            _selected = _stops.IndexOf(moved);
            Commit();
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        int hit = HitStop(p.X);
        if (hit >= 0)
            EditColor(hit);
    }

    public void EditColor(int index)
    {
        if (index < 0 || index >= _stops.Count) return;
        ColorPickerFlyout.Show(this, _stops[index].Rgba, false, c =>
        {
            _stops[index] = _stops[index] with { Color = c.ToRgbString() };
            Commit();
        });
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Delete or Windows.System.VirtualKey.Back:
                RemoveSelected();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space:
                EditColor(_selected);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right when _selected >= 0:
                double delta = e.Key == Windows.System.VirtualKey.Left ? -0.01 : 0.01;
                double pos = Math.Clamp(_stops[_selected].Position + delta, 0, 1);
                var s = _stops[_selected] with { Position = Math.Round(pos, 3) };
                _stops[_selected] = s;
                _stops = _stops.OrderBy(x => x.Position).ToList();
                _selected = _stops.IndexOf(s);
                Commit();
                e.Handled = true;
                break;
        }
    }

    private void Commit()
    {
        _canvas.Invalidate();
        StopsChanged?.Invoke(_stops);
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        float h = (float)sender.ActualHeight;
        float barH = 14, barY = (h - barH) / 2;
        var stops = _stops.Select(s => new CanvasGradientStop { Position = (float)s.Position, Color = ColorUtil.ToColor(s.Rgba) }).ToArray();
        using (var brush = new CanvasLinearGradientBrush(sender, stops)
        {
            StartPoint = new Vector2(BarLeft, 0),
            EndPoint = new Vector2(BarLeft + BarWidth, 0),
        })
        {
            ds.FillRoundedRectangle(BarLeft, barY, BarWidth, barH, barH / 2, barH / 2, brush);
        }

        var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
        for (int i = 0; i < _stops.Count; i++)
        {
            var center = new Vector2(BarLeft + (float)_stops[i].Position * BarWidth, h / 2);
            ds.FillCircle(center, HandleRadius - 1, Microsoft.UI.Colors.White);
            ds.FillCircle(center, HandleRadius - 4, ColorUtil.ToColor(_stops[i].Rgba));
            ds.DrawCircle(center, HandleRadius - 1, i == _selected ? accent : Windows.UI.Color.FromArgb(0x60, 0, 0, 0), i == _selected ? 2.5f : 1f);
        }
    }
}
