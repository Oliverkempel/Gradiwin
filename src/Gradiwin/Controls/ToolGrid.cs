// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Presets;
using Gradiwin.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Gradiwin.Controls;

/// <summary>Port of widget/drawing_tools_grid.py: 5×2 grid of tool toggles.</summary>
public sealed partial class ToolGrid : Grid
{
    private readonly Dictionary<DrawingMode, ToggleButton> _buttons = [];
    private bool _updating;
    private DrawingMode? _secondary;

    public event Action<ToolDefinition>? ToolChanged;

    public ToolGrid()
    {
        ColumnSpacing = 4;
        RowSpacing = 4;
        for (int i = 0; i < 5; i++) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 2; i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var tool in ToolDefinition.All)
        {
            var button = new ToggleButton
            {
                Content = new SymbolicIcon(tool.Icon, 18),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 40,
                Padding = new Thickness(0),
            };
            var shortcuts = string.Join(", ", tool.Shortcuts.Select(s => s.ToUpperInvariant()));
            ToolTipService.SetToolTip(button, $"{Localizer.T(tool.Label)} ({shortcuts})");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Localizer.T(tool.Label));
            SetColumn(button, tool.Column);
            SetRow(button, tool.Row);
            var t = tool;
            button.Checked += (_, _) =>
            {
                if (_updating) return;
                SetCurrent(t.Mode);
                ToolChanged?.Invoke(t);
            };
            button.Unchecked += (_, _) =>
            {
                // Radio behavior: keep the current tool checked.
                if (!_updating && Current == t.Mode)
                {
                    _updating = true;
                    button.IsChecked = true;
                    _updating = false;
                }
            };
            _buttons[tool.Mode] = button;
            Children.Add(button);
        }
    }

    public DrawingMode Current { get; private set; } = DrawingMode.Select;

    public void SetCurrent(DrawingMode mode)
    {
        _updating = true;
        Current = mode;
        foreach (var (m, b) in _buttons)
            b.IsChecked = m == mode;
        _updating = false;
        UpdateSecondary();
    }

    /// <summary>Highlights the tool of the selected annotation while editing it (Gradia's secondary highlight).</summary>
    public void SetSecondaryHighlight(DrawingMode? mode)
    {
        _secondary = mode;
        UpdateSecondary();
    }

    private void UpdateSecondary()
    {
        foreach (var (m, b) in _buttons)
        {
            b.BorderBrush = _secondary == m && m != Current
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : null;
            b.BorderThickness = _secondary == m && m != Current ? new Thickness(2) : new Thickness(1);
        }
    }
}
