// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Models;
using Gradiwin.Core.Presets;
using Gradiwin.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gradiwin.Controls;

/// <summary>Port of widget/quick_color_picker.py QuickColorPicker: a row of preset colors plus "More Colors".</summary>
public sealed partial class QuickColorPicker : StackPanel
{
    private readonly List<(Button Button, ColorSwatch Swatch, NamedColor Color)> _buttons = [];
    private readonly Button _more;
    private readonly CustomColorSwatch _customSwatch;
    private RgbaColor _color = RgbaColor.Black;

    public event Action<QuickColorPicker, RgbaColor>? ColorChanged;

    public QuickColorPicker()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        Spacing = 2;

        _customSwatch = new CustomColorSwatch(22);
        _more = new Button
        {
            Content = _customSwatch,
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(_more, Localizer.T("More Colors"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_more, Localizer.T("More Colors"));
        _more.Click += (_, _) => ColorPickerFlyout.Show(_more, _color, WithAlpha, c =>
        {
            SetColor(c);
            ColorChanged?.Invoke(this, c);
        });
        SetPalette(Palettes.BaseColors());
    }

    public bool WithAlpha { get; set; } = true;

    public RgbaColor Color => _color;

    public int SelectedIndex => _buttons.FindIndex(b => b.Swatch.IsSelected);

    public void SetPalette(IReadOnlyList<NamedColor>? palette)
    {
        palette ??= Palettes.BaseColors();
        foreach (var b in _buttons)
            Children.Remove(b.Button);
        Children.Remove(_more);
        _buttons.Clear();

        foreach (var named in palette)
        {
            var swatch = new ColorSwatch(22) { Color = named.Color };
            var button = new Button
            {
                Content = swatch,
                Padding = new Thickness(5),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(button, Localizer.T(named.Name));
            var captured = named.Color;
            button.Click += (_, _) =>
            {
                SetColor(captured);
                ColorChanged?.Invoke(this, captured);
            };
            _buttons.Add((button, swatch, named));
            Children.Add(button);
        }
        Children.Add(_more);
        SetColor(_color);
    }

    public void SetColor(RgbaColor color)
    {
        _color = color;
        bool matched = false;
        foreach (var b in _buttons)
        {
            bool m = !matched && Close(b.Color.Color, color);
            b.Swatch.IsSelected = m;
            matched |= m;
        }
        _customSwatch.CustomColor = matched ? null : color;
        _customSwatch.IsSelected = !matched;
    }

    internal static bool Close(RgbaColor a, RgbaColor b) =>
        Math.Abs(a.R - b.R) < 0.01 && Math.Abs(a.G - b.G) < 0.01 && Math.Abs(a.B - b.B) < 0.01 && Math.Abs(a.A - b.A) < 0.01;
}

/// <summary>Port of SimpleColorPicker: an icon button with a color chip that opens a palette.</summary>
public sealed partial class SimpleColorPicker : Button
{
    private readonly ColorSwatch _chip = new(16);
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<NamedColor> _palette = Palettes.BaseColors(secondary: true);
    private RgbaColor _color = RgbaColor.White;

    public event Action<SimpleColorPicker, RgbaColor>? ColorChanged;

    public SimpleColorPicker(string icon, string? label)
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new SymbolicIcon(icon, 16) { VerticalAlignment = VerticalAlignment.Center });
        if (label != null)
        {
            _label.Text = label;
            content.Children.Add(_label);
        }
        content.Children.Add(_chip);
        Content = content;
        Click += (_, _) => ShowPalette();
    }

    public RgbaColor Color => _color;

    public void SetPalette(IReadOnlyList<NamedColor>? palette) => _palette = palette ?? Palettes.BaseColors(secondary: true);

    public void SetColor(RgbaColor color)
    {
        _color = color;
        _chip.Color = color;
    }

    public void SetColorByIndex(int index)
    {
        if (index >= 0 && index < _palette.Count)
        {
            SetColor(_palette[index].Color);
            ColorChanged?.Invoke(this, _color);
        }
    }

    private void ShowPalette()
    {
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 4, ItemWidth = 40, ItemHeight = 40 };
        var flyout = new Flyout();
        foreach (var named in _palette)
        {
            var swatch = new ColorSwatch(26) { Color = named.Color, IsSelected = QuickColorPicker.Close(named.Color, _color) };
            var b = new Button { Content = swatch, Padding = new Thickness(4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            ToolTipService.SetToolTip(b, Localizer.T(named.Name));
            var c = named.Color;
            b.Click += (_, _) =>
            {
                SetColor(c);
                ColorChanged?.Invoke(this, c);
                flyout.Hide();
            };
            grid.Children.Add(b);
        }
        var more = new Button { Content = new CustomColorSwatch(26), Padding = new Thickness(4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
        ToolTipService.SetToolTip(more, Localizer.T("More Colors"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, Localizer.T("More Colors"));
        more.Click += (_, _) =>
        {
            flyout.Hide();
            ColorPickerFlyout.Show(this, _color, true, c =>
            {
                SetColor(c);
                ColorChanged?.Invoke(this, c);
            });
        };
        grid.Children.Add(more);
        flyout.Content = grid;
        flyout.ShowAt(this);
    }
}
