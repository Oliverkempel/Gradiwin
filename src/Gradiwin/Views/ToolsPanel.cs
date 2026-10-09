// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Controls;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;
using Gradiwin.Core.Presets;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Gradiwin.Views;

/// <summary>Port of ui/drawing_tools_group.py ("Annotation Tools").</summary>
public sealed partial class ToolsPanel : UserControl
{
    private static IReadOnlyList<string>? s_fonts;

    private readonly Dictionary<DrawingMode, ToolOptions> _tools = [];
    private readonly ToolGrid _grid = new();
    private readonly QuickColorPicker _primary = new();
    private readonly Slider _size;
    private readonly StackPanel _fillPanel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly SimpleColorPicker _fill = new("fill", Localizer.T("Fill"));
    private readonly SimpleColorPicker _border = new("outline-color", Localizer.T("Border"));
    private readonly ComboBox _font;
    private readonly Grid _extra = new() { ColumnSpacing = 8, RowSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };

    private ToolDefinition _currentDef = ToolDefinition.For(DrawingMode.Select);
    private ToolOptions? _tempOptions;
    private ToolDefinition? _tempDef;
    private bool _updating;

    /// <summary>The user picked another tool.</summary>
    public event Action<DrawingMode>? ToolChanged;

    /// <summary>An option changed (redraw needed).</summary>
    public event Action? OptionsChanged;

    public ToolsPanel()
    {
        var settings = SettingsService.Instance;
        foreach (var mode in Enum.GetValues<DrawingMode>())
        {
            var options = settings.GetToolOptions(mode);
            options.Changed += o => settings.SetToolOptions(o);
            _tools[mode] = options;
        }

        _size = new Slider
        {
            Minimum = ToolOptions.MinSize,
            Maximum = ToolOptions.MaxSize,
            StepFrequency = 1,
            SmallChange = 2,
            LargeChange = 2,
            TickFrequency = 2,
            TickPlacement = TickPlacement.None,
            Margin = new Thickness(4, 2, 4, 0),
        };
        ToolTipService.SetToolTip(_size, Localizer.T("Size"));

        _font = new ComboBox
        {
            ItemsSource = Fonts,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTextSearchEnabled = true,
            MaxDropDownHeight = 400,
        };
        ToolTipService.SetToolTip(_font, Localizer.T("Font"));

        // Two equal columns; the text tool's font picker gets its own full-width row below.
        _extra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _extra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _extra.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _extra.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var root = new StackPanel { Spacing = 6 };
        root.Children.Add(new TextBlock { Text = Localizer.T("Annotation Tools"), Style = (Style)Application.Current.Resources["SidebarHeaderStyle"], Margin = new Thickness(2, 0, 0, 6) });
        root.Children.Add(_grid);
        root.Children.Add(_primary);
        root.Children.Add(_size);
        root.Children.Add(_extra);
        Content = root;

        _grid.ToolChanged += def =>
        {
            SetCurrentTool(def.Mode);
            ToolChanged?.Invoke(def.Mode);
        };
        _primary.ColorChanged += OnPrimaryChanged;
        _size.ValueChanged += (_, e) =>
        {
            if (_updating || ActiveOptions is not { } o) return;
            o.Size = (int)e.NewValue;
            OptionsChanged?.Invoke();
        };
        _fill.ColorChanged += (_, c) =>
        {
            if (_updating || ActiveOptions is not { } o) return;
            o.FillColor = c;
            OptionsChanged?.Invoke();
        };
        _border.ColorChanged += (_, c) =>
        {
            if (_updating || ActiveOptions is not { } o) return;
            o.BorderColor = c;
            OptionsChanged?.Invoke();
        };
        _font.SelectionChanged += (_, _) =>
        {
            if (_updating || ActiveOptions is not { } o || _font.SelectedItem is not string f) return;
            o.Font = f;
            OptionsChanged?.Invoke();
        };

        SetCurrentTool(settings.DrawMode);
    }

    private static IReadOnlyList<string> Fonts => s_fonts ??= CanvasTextFormat.GetSystemFontFamilies()
        .Distinct().OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();

    public DrawingMode CurrentMode => _currentDef.Mode;

    public ToolOptions GetOptions(DrawingMode mode) => _tools[mode];

    private ToolOptions? ActiveOptions => _tempOptions ?? _tools[_currentDef.Mode];
    private ToolDefinition ActiveDef => _tempDef ?? _currentDef;

    public void SetCurrentTool(DrawingMode mode)
    {
        if (_tempOptions != null)
            ExitTempEditing();
        _currentDef = ToolDefinition.For(mode);
        _grid.SetCurrent(mode);
        SettingsService.Instance.DrawMode = mode;
        UpdateUi(_currentDef, _tools[mode]);
    }

    /// <summary>Selected annotation: edit its options in place (Gradia's "temp editing mode").</summary>
    public void EnterTempEditing(Annotation annotation)
    {
        _tempOptions = annotation.Options;
        _tempDef = ToolDefinition.For(annotation.Mode);
        UpdateUi(_tempDef, _tempOptions);
        _grid.SetSecondaryHighlight(annotation.Mode);
    }

    public void ExitTempEditing()
    {
        if (_tempOptions is null) return;
        _tempOptions = null;
        _tempDef = null;
        _grid.SetSecondaryHighlight(null);
        UpdateUi(_currentDef, _tools[_currentDef.Mode]);
    }

    /// <summary>Wheel-driven size change (DrawingToolsGroup._on_scroll).</summary>
    public void AdjustSize(int delta)
    {
        if (ActiveOptions is not { } o || !ActiveDef.HasScale) return;
        int size = Math.Clamp(o.Size + delta, ToolOptions.MinSize, ToolOptions.MaxSize);
        if (size == o.Size) return;
        o.Size = size;
        _updating = true;
        _size.Value = size;
        _updating = false;
        OptionsChanged?.Invoke();
    }

    private void OnPrimaryChanged(QuickColorPicker picker, RgbaColor color)
    {
        if (_updating || ActiveOptions is not { } o) return;
        if (ActiveDef.MatchPrimaryToSecondary)
        {
            // Number stamps: pick a matching background and clear the outline.
            int index = picker.SelectedIndex;
            if (index >= 0)
            {
                _fill.SetPalette(ActiveDef.SecondaryColors);
                _fill.SetColorByIndex(index);
            }
            o.BorderColor = RgbaColor.Transparent;
            _border.SetColor(RgbaColor.Transparent);
        }
        o.PrimaryColor = color;
        OptionsChanged?.Invoke();
    }

    private void UpdateUi(ToolDefinition def, ToolOptions options)
    {
        _updating = true;
        _size.IsEnabled = def.HasScale;
        _primary.IsHitTestVisible = def.HasPrimaryColor;
        _primary.Opacity = def.HasPrimaryColor ? 1 : 0.4;
        _primary.SetPalette(def.PrimaryColors);
        _fill.SetPalette(def.SecondaryColors);
        _border.SetPalette(def.SecondaryColors);

        if (def.HasScale) _size.Value = options.Size;
        _primary.SetColor(options.PrimaryColor);
        _fill.SetColor(options.FillColor);
        _border.SetColor(options.BorderColor);
        _font.SelectedItem = Fonts.Contains(options.Font) ? options.Font : Fonts.FirstOrDefault(f => f.StartsWith("Segoe UI", StringComparison.Ordinal));

        _extra.Children.Clear();
        switch (def.Panel)
        {
            case ToolPanel.Fill:
                Place(_fill, 0, 0, 2);
                break;
            case ToolPanel.FillBorder:
                Place(_fill, 0, 0, 1);
                Place(_border, 0, 1, 1);
                break;
            case ToolPanel.Text:
                Place(_fill, 0, 0, 1);
                Place(_border, 0, 1, 1);
                Place(_font, 1, 0, 2);
                break;
        }
        _extra.Visibility = def.Panel == ToolPanel.Empty ? Visibility.Collapsed : Visibility.Visible;
        _updating = false;
    }

    private void Place(FrameworkElement e, int row, int column, int span)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
        _extra.Children.Add(e);
    }
}
