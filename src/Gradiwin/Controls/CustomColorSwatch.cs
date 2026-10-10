// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Models;
using Gradiwin.Rendering;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gradiwin.Controls;

/// <summary>
/// The "More colors" swatch: a rainbow (conic gradient) ring like Gradia's color-picker button.
/// When the current color isn't one of the presets it is shown in the middle of the ring, with the same
/// checkmark the preset swatches use when selected.
/// </summary>
public sealed partial class CustomColorSwatch : Grid
{
    private static readonly Gradient s_hues = new(GradientMode.Conic,
        [
            new(0.0, "#ff0000"), new(1 / 6.0, "#ffff00"), new(2 / 6.0, "#00ff00"),
            new(3 / 6.0, "#00ffff"), new(4 / 6.0, "#0000ff"), new(5 / 6.0, "#ff00ff"), new(1.0, "#ff0000"),
        ], 0);

    private readonly CanvasControl _canvas;
    private readonly FontIcon _check;
    private CanvasBitmap? _ring;
    private RgbaColor? _custom;

    public CustomColorSwatch(double size)
    {
        Width = size;
        Height = size;
        _canvas = new CanvasControl { ClearColor = Colors.Transparent };
        _canvas.CreateResources += (s, _) =>
        {
            // Rendered once at 4x for crisp edges; the conic generator spans the full hue circle.
            int px = (int)Math.Ceiling(size * 4);
            var pixels = GradientGenerator.GenerateBgra(s_hues, px, px);
            _ring = CanvasBitmap.CreateFromBytes(s, pixels, px, px, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 96 * 4, CanvasAlphaMode.Premultiplied);
        };
        _canvas.Draw += OnDraw;
        _check = new FontIcon
        {
            Glyph = "",
            FontSize = size * 0.42,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(_canvas);
        Children.Add(_check);
        Unloaded += (_, _) => _canvas.RemoveFromVisualTree();
    }

    /// <summary>A custom (non-preset) color to show inside the ring, or null for the plain ring.</summary>
    public RgbaColor? CustomColor
    {
        get => _custom;
        set
        {
            _custom = value;
            _check.Foreground = new SolidColorBrush(value is { } c && !c.IsLight ? Colors.White : Colors.Black);
            _canvas.Invalidate();
        }
    }

    public bool IsSelected
    {
        get => _check.Visibility == Visibility.Visible;
        set => _check.Visibility = value && _custom is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_ring is null) return;
        var ds = args.DrawingSession;
        float r = (float)Math.Min(sender.ActualWidth, sender.ActualHeight) / 2;
        var center = new Vector2(r, r);

        using (var brush = new CanvasImageBrush(sender, _ring)
        {
            SourceRectangle = new Windows.Foundation.Rect(0, 0, 2 * r, 2 * r),
        })
        {
            ds.FillCircle(center, r, brush);
        }

        if (_custom is { } c)
        {
            // Custom color inside the ring, separated by a thin neutral gap so it reads as "chosen".
            float inner = r * 0.62f;
            ds.FillCircle(center, inner + 2, ActualTheme == ElementTheme.Light ? Colors.White : Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20));
            ds.FillCircle(center, inner, c.A <= 0 ? Windows.UI.Color.FromArgb(255, 0xe8, 0xe8, 0xe8) : ColorUtil.SwatchColor(c));
        }
    }
}
