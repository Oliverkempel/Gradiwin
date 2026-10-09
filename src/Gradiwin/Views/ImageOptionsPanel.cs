// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Algorithms;
using Layout = Gradiwin.Core.Algorithms.Layout;
using Gradiwin.Core.Models;
using Gradiwin.Core.Presets;
using Gradiwin.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Gradiwin.Views;

/// <summary>Port of ui/image_sidebar.py "Image Options" plus background_aspect_ratio_selector.py.</summary>
public sealed partial class ImageOptionsPanel : UserControl
{
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly NumberBox _padding;
    private readonly NumberBox _radius;
    private readonly Slider _shadow;
    private readonly ToggleSwitch _balance;
    private readonly DropDownButton _aspect;
    private readonly StackPanel _disabledWhenNoBackground = new() { Spacing = 8 };
    private string _aspectRatio;
    private int _rotation;

    public event Action? Changed;

    public ImageOptionsPanel()
    {
        _aspectRatio = _settings.ImageAspectRatio;

        _padding = NewNumber(_settings.ImagePadding, 0, 75, 5);
        _padding.ValueChanged += (_, e) => OnValue(e.NewValue, v => _settings.ImagePadding = v);
        _radius = NewNumber(_settings.ImageCornerRadius, 0, 50, 1);
        _radius.ValueChanged += (_, e) => OnValue(e.NewValue, v => _settings.ImageCornerRadius = v);

        _shadow = new Slider
        {
            Minimum = 0,
            Maximum = 10,
            StepFrequency = 1,
            Value = _settings.ImageShadowStrength,
            Width = 150,
            TickPlacement = TickPlacement.None,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _shadow.ValueChanged += (_, e) => OnValue(e.NewValue, v => _settings.ImageShadowStrength = v);

        _balance = new ToggleSwitch { IsOn = _settings.ImageAutoBalance, OnContent = "", OffContent = "", MinWidth = 0 };
        _balance.Toggled += (_, _) =>
        {
            _settings.ImageAutoBalance = _balance.IsOn;
            Changed?.Invoke();
        };

        _aspect = new DropDownButton { MinWidth = 96 };
        UpdateAspectLabel();
        _aspect.Flyout = BuildAspectMenu();

        var rotateLeft = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Padding = new Thickness(8) };
        ToolTipService.SetToolTip(rotateLeft, Localizer.T("Rotate Anticlockwise"));
        rotateLeft.Click += (_, _) => Rotate(90);
        var rotateRight = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5), RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = -1 } },
            Padding = new Thickness(8),
        };
        ToolTipService.SetToolTip(rotateRight, Localizer.T("Rotate Clockwise"));
        rotateRight.Click += (_, _) => Rotate(270);

        _disabledWhenNoBackground.Children.Add(Row("_Padding", _padding));
        _disabledWhenNoBackground.Children.Add(Row("_Corner Radius", _radius));
        _disabledWhenNoBackground.Children.Add(Row("Aspect Ratio", _aspect));
        _disabledWhenNoBackground.Children.Add(Row("_Shadow", _shadow));

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock { Text = Localizer.T("Image Options"), Style = (Style)Application.Current.Resources["SidebarHeaderStyle"] });
        root.Children.Add(_disabledWhenNoBackground);
        root.Children.Add(Row("_Auto Balance", _balance));
        root.Children.Add(Row("_Rotation", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { rotateLeft, rotateRight } }));
        Content = root;
    }

    private static NumberBox NewNumber(int value, int min, int max, int step) => new()
    {
        Minimum = min,
        Maximum = max,
        SmallChange = step,
        LargeChange = step,
        Value = value,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        MinWidth = 120,
    };

    private static Grid Row(string label, FrameworkElement control)
    {
        var g = new Grid { MinHeight = 40, Padding = new Thickness(12, 4, 8, 4), CornerRadius = new CornerRadius(6) };
        g.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = Localizer.StripMnemonic(Localizer.T(label)), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private void OnValue(double v, Action<int> store)
    {
        if (double.IsNaN(v)) return;
        store((int)Math.Round(v));
        Changed?.Invoke();
    }

    private void Rotate(int delta)
    {
        _rotation = (_rotation + delta) % 360;
        Changed?.Invoke();
    }

    public void ResetRotation() => _rotation = 0;

    /// <summary>Background "none" disables padding/radius/aspect/shadow (image_sidebar._set_selective_visiblility).</summary>
    public void SetBackgroundEnabled(bool enabled)
    {
        _disabledWhenNoBackground.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    public ImageOptions GetOptions(bool backgroundEnabled)
    {
        var o = new ImageOptions
        {
            Padding = _settings.ImagePadding,
            CornerRadius = _settings.ImageCornerRadius,
            AspectRatio = _aspectRatio,
            ShadowStrength = _settings.ImageShadowStrength,
            AutoBalance = _settings.ImageAutoBalance,
            Rotation = _rotation,
        };
        return backgroundEnabled ? o : o.ForNoBackground();
    }

    private void UpdateAspectLabel() =>
        _aspect.Content = string.IsNullOrEmpty(_aspectRatio) ? Localizer.T("Auto") : _aspectRatio;

    private MenuFlyout BuildAspectMenu()
    {
        var menu = new MenuFlyout();
        foreach (var (label, value) in Palettes.BackgroundAspectRatios)
        {
            var item = new MenuFlyoutItem { Text = value.Length == 0 ? Localizer.T(label) : label };
            var v = value;
            item.Click += (_, _) => SetAspect(v);
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var custom = new MenuFlyoutItem { Text = Localizer.T("Custom…") };
        custom.Click += async (_, _) => await AskCustomAspectAsync();
        menu.Items.Add(custom);
        return menu;
    }

    private void SetAspect(string value)
    {
        _aspectRatio = value;
        _settings.ImageAspectRatio = value;
        UpdateAspectLabel();
        Changed?.Invoke();
    }

    private async Task AskCustomAspectAsync()
    {
        if (XamlRoot is null) return;
        var w = new NumberBox { Header = Localizer.T("Width"), Minimum = 1, Maximum = 100, Value = 16, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var h = new NumberBox { Header = Localizer.T("Height"), Minimum = 1, Maximum = 100, Value = 9, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var error = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], Visibility = Visibility.Collapsed };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.T("Custom Aspect Ratio"),
            Content = new StackPanel { Spacing = 12, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { w, h } }, error } },
            PrimaryButtonText = Localizer.T("Apply"),
            CloseButtonText = Localizer.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            double ratio = w.Value / h.Value;
            if (double.IsNaN(ratio) || !Layout.IsAspectRatioInBounds(ratio))
            {
                error.Text = Localizer.T("Aspect ratio must be between 0.2 and 5");
                error.Visibility = Visibility.Visible;
                e.Cancel = true;
            }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            SetAspect($"{(int)w.Value}:{(int)h.Value}");
    }
}
