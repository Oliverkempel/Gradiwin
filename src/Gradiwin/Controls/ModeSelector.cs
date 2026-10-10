// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gradiwin.Controls;

/// <summary>
/// A segmented selector (None / Solid / Gradient / Image, Linear / Radial / Conic) built from plain buttons.
/// Hover and press fill the whole segment (the toolkit's Segmented only highlighted an inset area), and the
/// selected segment gets a subtle fill plus an accent indicator underneath its content.
/// </summary>
public sealed partial class ModeSelector : UserControl
{
    public sealed record Item(UIElement Content, string Name);

    private readonly List<(Button Button, Border Indicator)> _segments = [];
    private int _selectedIndex = -1;

    public event Action<int>? SelectionChanged;

    /// <param name="padding">Padding inside each segment.</param>
    public ModeSelector(IReadOnlyList<Item> items, Thickness padding)
    {
        var res = Application.Current.Resources;
        var grid = new Grid { ColumnSpacing = 2 };

        for (int i = 0; i < items.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var indicator = new Border
            {
                Width = 16,
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = (Brush)res["AccentFillColorDefaultBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                Opacity = 0,
            };
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(items[i].Content);
            content.Children.Add(indicator);

            var button = new Button
            {
                Content = content,
                Padding = padding,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(0),
            };
            // Lightweight styling: transparent at rest, standard subtle fills on hover/press across the full cell.
            button.Resources["ButtonBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Resources["ButtonBackgroundPointerOver"] = res["SubtleFillColorSecondaryBrush"];
            button.Resources["ButtonBackgroundPressed"] = res["SubtleFillColorTertiaryBrush"];
            button.Resources["ButtonBorderBrush"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ToolTipService.SetToolTip(button, items[i].Name);
            AutomationProperties.SetName(button, items[i].Name);

            int index = i;
            button.Click += (_, _) =>
            {
                if (index == _selectedIndex) return;
                SelectedIndex = index;
                SelectionChanged?.Invoke(index);
            };

            Grid.SetColumn(button, i);
            grid.Children.Add(button);
            _segments.Add((button, indicator));
        }

        Content = new Border
        {
            Child = grid,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)res["ControlAltFillColorSecondaryBrush"],
            BorderBrush = (Brush)res["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>Selected segment; setting it updates the visuals without raising <see cref="SelectionChanged"/>.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            var selectedFill = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
            for (int i = 0; i < _segments.Count; i++)
            {
                bool selected = i == value;
                var (button, indicator) = _segments[i];
                // A local Background applies to the rest state only, so hover still shows the hover fill.
                button.Background = selected ? selectedFill : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                indicator.Opacity = selected ? 1 : 0;
            }
        }
    }
}
