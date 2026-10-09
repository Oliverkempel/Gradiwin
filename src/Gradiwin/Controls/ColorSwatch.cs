// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Models;
using Gradiwin.Rendering;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gradiwin.Controls;

/// <summary>A round color chip; transparent colors show a checkerboard with a slash, like Gradia's buttons.</summary>
public sealed partial class ColorSwatch : Grid
{
    private readonly Border _fill;
    private readonly Border _check;
    private readonly FontIcon _checkGlyph;
    private readonly Microsoft.UI.Xaml.Shapes.Line _slash;
    private RgbaColor _color;
    private bool _selected;

    public ColorSwatch(double size = 24)
    {
        Width = size;
        Height = size;
        _fill = new Border
        {
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
        };
        _slash = new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = size * 0.2,
            Y1 = size * 0.8,
            X2 = size * 0.8,
            Y2 = size * 0.2,
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xc0, 0x1c, 0x28)),
            StrokeThickness = 2,
            Visibility = Visibility.Collapsed,
        };
        _checkGlyph = new FontIcon { Glyph = "", FontSize = size * 0.5 };
        _check = new Border { Child = _checkGlyph, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(_fill);
        Children.Add(_slash);
        Children.Add(_check);
    }

    public RgbaColor Color
    {
        get => _color;
        set
        {
            _color = value;
            _fill.Background = value.A <= 0
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xe8, 0xe8, 0xe8))
                : new SolidColorBrush(ColorUtil.SwatchColor(value));
            _slash.Visibility = value.A <= 0 ? Visibility.Visible : Visibility.Collapsed;
            _checkGlyph.Foreground = new SolidColorBrush(value.IsLight ? Colors.Black : Colors.White);
        }
    }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            _check.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
