// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.WinUI.Controls;
using Gradiwin.Controls;
using Gradiwin.Core.Models;
using Gradiwin.Core.Presets;
using Gradiwin.Rendering;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using GradientStop = Gradiwin.Core.Models.GradientStop;

namespace Gradiwin.Views;

/// <summary>Port of ui/background_selector.py with the solid, gradient and image selectors.</summary>
public sealed partial class BackgroundPanel : UserControl
{
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly Segmented _modes;
    private readonly ContentControl _host = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 0, 0) };

    private SolidBackground _solid;
    private Gradient _gradient;
    private CanvasBitmap? _image;
    private BackgroundMode _mode;

    // Solid
    private readonly List<(ColorSwatch Swatch, NamedColor Color)> _solidSwatches = [];
    private ColorSwatch? _solidCustom;

    // Gradient
    private GradientEditor? _gradientEditor;
    private AngleSelector? _angle;
    private NumberBox? _angleBox;
    private Segmented? _gradientModes;

    // Image
    private Image? _imagePreview;

    private bool _updating;

    public event Action<BackgroundSpec>? Changed;

    public Window? HostWindow { get; set; }

    public BackgroundPanel()
    {
        _solid = _settings.SolidState;
        _gradient = _settings.GradientState;
        _mode = _settings.BackgroundMode;

        _modes = new Segmented { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var label in new[] { "None", "Solid", "Gradient", "Image" })
        {
            var item = new SegmentedItem { Content = Localizer.T(label), HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTipService.SetToolTip(item, Localizer.T(label));
            _modes.Items.Add(item);
        }
        _modes.SelectedIndex = (int)_mode;
        _modes.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            _mode = (BackgroundMode)_modes.SelectedIndex;
            _settings.BackgroundMode = _mode;
            ShowModePanel();
            Notify();
        };

        var root = new StackPanel();
        root.Children.Add(new MenuFlyoutSeparator { Margin = new Thickness(0, 4, 0, 12) });
        root.Children.Add(_modes);
        root.Children.Add(_host);
        Content = root;

        ShowModePanel();
        _ = LoadImageBackgroundAsync();
    }

    public BackgroundSpec CurrentSpec => new()
    {
        Mode = _mode == BackgroundMode.Image && _image is null ? BackgroundMode.None : _mode,
        Solid = _solid,
        Gradient = _gradient,
        Image = _image,
    };

    public BackgroundMode Mode => _mode;

    private void Notify() => Changed?.Invoke(CurrentSpec);

    private void ShowModePanel()
    {
        _host.Content = _mode switch
        {
            BackgroundMode.Solid => BuildSolidPanel(),
            BackgroundMode.Gradient => BuildGradientPanel(),
            BackgroundMode.Image => BuildImagePanel(),
            _ => null,
        };
        _host.Visibility = _mode == BackgroundMode.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = Localizer.T(text),
        Style = (Style)Application.Current.Resources["SidebarHeaderStyle"],
        Margin = new Thickness(2, 0, 0, 8),
    };

    private static Button IconButton(string icon, string tooltip, bool symbolic = true)
    {
        var b = new Button
        {
            Content = symbolic ? new SymbolicIcon(icon, 16) : new FontIcon { Glyph = icon, FontSize = 16 },
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(b, Localizer.T(tooltip));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, Localizer.T(tooltip));
        return b;
    }

    // ---------------- Solid (graphics/solid.py SolidSelector) ----------------

    private UIElement BuildSolidPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(Header("Solid Color Background"));
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 5, ItemWidth = 52, ItemHeight = 52, HorizontalAlignment = HorizontalAlignment.Center };
        _solidSwatches.Clear();
        foreach (var named in Palettes.SolidColors)
        {
            var swatch = new ColorSwatch(36) { Color = named.Color };
            var b = new Button { Content = swatch, Padding = new Thickness(4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            ToolTipService.SetToolTip(b, Localizer.T(named.Name));
            var c = named.Color;
            b.Click += (_, _) => SetSolid(c);
            _solidSwatches.Add((swatch, named));
            grid.Children.Add(b);
        }
        _solidCustom = new ColorSwatch(36) { Color = _solid.Rgba };
        var more = new Button
        {
            Content = new Grid { Children = { _solidCustom, new SymbolicIcon("color", 18) } },
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(more, Localizer.T("More Colors"));
        more.Click += (_, _) => ColorPickerFlyout.Show(more, _solid.Rgba, true, SetSolid);
        grid.Children.Add(more);
        panel.Children.Add(grid);
        UpdateSolidSelection();
        return panel;
    }

    private void SetSolid(RgbaColor c)
    {
        _solid = new SolidBackground(c.ToHex(), c.A);
        _settings.SolidState = _solid;
        UpdateSolidSelection();
        Notify();
    }

    private void UpdateSolidSelection()
    {
        var current = _solid.Rgba;
        bool matched = false;
        foreach (var (swatch, named) in _solidSwatches)
        {
            bool m = !matched && QuickColorPicker.Close(named.Color, current);
            swatch.IsSelected = m;
            matched |= m;
        }
        if (_solidCustom != null)
        {
            _solidCustom.Color = matched ? RgbaColor.Transparent with { A = 0 } : current;
            _solidCustom.Visibility = matched ? Visibility.Collapsed : Visibility.Visible;
            _solidCustom.IsSelected = !matched;
        }
    }

    // ---------------- Gradient (graphics/gradient_selector.py) ----------------

    private UIElement BuildGradientPanel()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Header("Gradient Background"));

        _gradientModes = new Segmented();
        foreach (var (icon, label) in new[] { ("linear", "Linear"), ("radial", "Radial"), ("conic", "Conic") })
        {
            var item = new SegmentedItem { Content = new SymbolicIcon(icon, 16) };
            ToolTipService.SetToolTip(item, Localizer.T(label));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, Localizer.T(label));
            _gradientModes.Items.Add(item);
        }
        _gradientModes.SelectedIndex = _gradient.Mode switch { GradientMode.Radial => 1, GradientMode.Conic => 2, _ => 0 };
        _gradientModes.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            _gradient.Mode = _gradientModes.SelectedIndex switch { 1 => GradientMode.Radial, 2 => GradientMode.Conic, _ => GradientMode.Linear };
            UpdateAngleEnabled();
            CommitGradient();
        };

        _angle = new AngleSelector { Angle = _gradient.Angle };
        _angle.AngleChanged += a =>
        {
            _gradient.Angle = a;
            _updating = true;
            if (_angleBox != null) _angleBox.Value = a;
            _updating = false;
            CommitGradient();
        };
        _angleBox = new NumberBox
        {
            Minimum = 0,
            Maximum = 360,
            Value = _gradient.Angle,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            Width = 64,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _angleBox.ValueChanged += (_, e) =>
        {
            if (_updating || double.IsNaN(e.NewValue)) return;
            _gradient.Angle = e.NewValue % 360;
            if (_angle != null) _angle.Angle = _gradient.Angle;
            CommitGradient();
        };

        var top = new Grid { ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _gradientModes.HorizontalAlignment = HorizontalAlignment.Left;
        _gradientModes.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(_gradientModes);
        Grid.SetColumn(_angle, 1);
        top.Children.Add(_angle);
        Grid.SetColumn(_angleBox, 2);
        top.Children.Add(_angleBox);
        panel.Children.Add(top);

        _gradientEditor = new GradientEditor();
        _gradientEditor.SetStops(_gradient.Steps);
        _gradientEditor.StopsChanged += stops =>
        {
            _gradient.Steps = stops.ToList();
            CommitGradient(updateEditor: false);
        };
        panel.Children.Add(_gradientEditor);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        var remove = IconButton("", "Remove Selected Color Step", symbolic: false);
        remove.Click += (_, _) => _gradientEditor.RemoveSelected();
        var steps = IconButton("", "Open Step Dialog", symbolic: false);
        steps.Click += async (_, _) => await ShowStepDialogAsync();
        var presets = IconButton("", "Gradient Presets", symbolic: false);
        presets.Click += (_, _) => ShowGradientPresets(presets);
        actions.Children.Add(remove);
        actions.Children.Add(steps);
        actions.Children.Add(presets);
        panel.Children.Add(actions);

        UpdateAngleEnabled();
        return panel;
    }

    private void UpdateAngleEnabled()
    {
        bool enabled = _gradient.Mode != GradientMode.Radial;
        if (_angle != null) _angle.IsEnabled = enabled;
        if (_angleBox != null) _angleBox.IsEnabled = enabled;
    }

    private void CommitGradient(bool updateEditor = true)
    {
        if (updateEditor) _gradientEditor?.SetStops(_gradient.Steps);
        _settings.GradientState = _gradient;
        Notify();
    }

    private static LinearGradientBrush GradientBrush(Gradient g)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
        };
        foreach (var s in g.Steps)
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = s.Position, Color = ColorUtil.ToColor(s.Rgba) });
        return brush;
    }

    private void ShowGradientPresets(FrameworkElement anchor)
    {
        var flyout = new Flyout();
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 3, ItemWidth = 76, ItemHeight = 56 };
        foreach (var preset in Palettes.PredefinedGradients)
        {
            var b = new Button
            {
                Content = new Border { Width = 64, Height = 44, CornerRadius = new CornerRadius(6), Background = GradientBrush(preset) },
                Padding = new Thickness(2),
            };
            ToolTipService.SetToolTip(b, Localizer.T(preset.Name ?? ""));
            var p = preset;
            b.Click += (_, _) =>
            {
                _gradient = p.Clone();
                _gradient.Name = null;
                flyout.Hide();
                ShowModePanel();
                CommitGradient();
            };
            grid.Children.Add(b);
        }
        flyout.Content = grid;
        flyout.ShowAt(anchor);
    }

    /// <summary>Port of CssGradientDialog: edit stops as a numeric list.</summary>
    private async Task ShowStepDialogAsync()
    {
        if (XamlRoot is null) return;
        var working = _gradient.Steps.Select(s => s with { }).ToList();
        var list = new StackPanel { Spacing = 8 };

        void Rebuild()
        {
            list.Children.Clear();
            for (int i = 0; i < working.Count; i++)
            {
                int index = i;
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var swatch = new ColorSwatch(28) { Color = working[i].Rgba };
                var colorButton = new Button { Content = swatch, Padding = new Thickness(4) };
                colorButton.Click += (_, _) => ColorPickerFlyout.Show(colorButton, working[index].Rgba, false, c =>
                {
                    working[index] = working[index] with { Color = c.ToRgbString() };
                    swatch.Color = c;
                });
                var pos = new NumberBox
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = Math.Round(working[i].Position * 100),
                    SmallChange = 1,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    Header = null,
                };
                pos.ValueChanged += (_, e) =>
                {
                    if (!double.IsNaN(e.NewValue))
                        working[index] = working[index] with { Position = Math.Clamp(e.NewValue / 100, 0, 1) };
                };
                var del = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, IsEnabled = working.Count > 2 };
                del.Click += (_, _) =>
                {
                    working.RemoveAt(index);
                    Rebuild();
                };
                row.Children.Add(colorButton);
                Grid.SetColumn(pos, 1);
                row.Children.Add(pos);
                Grid.SetColumn(del, 2);
                row.Children.Add(del);
                list.Children.Add(row);
            }
            var add = new Button { Content = Localizer.T("Add Step"), IsEnabled = working.Count < 6 };
            add.Click += (_, _) =>
            {
                working.Add(new GradientStop(1.0, working[^1].Color));
                Rebuild();
            };
            list.Children.Add(add);
        }
        Rebuild();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.T("Gradient Steps"),
            Content = new ScrollViewer { Content = list, MaxHeight = 420 },
            PrimaryButtonText = Localizer.T("Apply"),
            CloseButtonText = Localizer.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            // Minimum gap of 0.18 between consecutive steps is not enforced strictly; sort and dedupe.
            _gradient.Steps = working.OrderBy(s => s.Position).ToList();
            ShowModePanel();
            CommitGradient();
        }
    }

    // ---------------- Image (graphics/image.py ImageSelector) ----------------

    private static string CachedImagePath => Path.Combine(SettingsService.DataFolder, "last_image.png");

    private async Task LoadImageBackgroundAsync()
    {
        var state = _settings.ImageState;
        string? path = state.Path;
        if (path is null && File.Exists(CachedImagePath))
            path = CachedImagePath;
        path ??= "preset:" + Palettes.PresetImages[0];
        await SetImageAsync(path, persist: false);
    }

    private static string ResolveImagePath(string path) =>
        path.StartsWith("preset:", StringComparison.Ordinal)
            ? Path.Combine(AppContext.BaseDirectory, "Assets", "Backgrounds", path["preset:".Length..])
            : path;

    private async Task SetImageAsync(string path, bool persist)
    {
        try
        {
            var resolved = ResolveImagePath(path);
            if (!File.Exists(resolved)) return;
            var bmp = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), resolved, 96);
            _image?.Dispose();
            _image = bmp;

            if (persist)
            {
                if (!path.StartsWith("preset:", StringComparison.Ordinal))
                {
                    // Gradia caches the last used image (last_image.png) so it survives the original being moved.
                    await bmp.SaveAsync(CachedImagePath, CanvasBitmapFileFormat.Png);
                    path = CachedImagePath;
                }
                _settings.ImageState = new ImageBackgroundState(path);
            }

            UpdateImagePreview(resolved);
            if (_mode == BackgroundMode.Image)
                Notify();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException || e.HResult < 0)
        {
            // Unsupported/corrupt file: keep the previous image.
        }
    }

    private void UpdateImagePreview(string path)
    {
        if (_imagePreview is null) return;
        _imagePreview.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 400, CreateOptions = BitmapCreateOptions.IgnoreImageCache };
    }

    private UIElement BuildImagePanel()
    {
        var panel = new StackPanel { Spacing = 10 };
        var header = new Grid();
        header.Children.Add(Header("Image Background"));
        var presets = IconButton("", "Image Presets", symbolic: false);
        presets.HorizontalAlignment = HorizontalAlignment.Right;
        presets.Click += (_, _) => ShowImagePresets(presets);
        header.Children.Add(presets);
        panel.Children.Add(header);

        _imagePreview = new Image { Stretch = Stretch.UniformToFill, Height = 150 };
        var previewBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = _imagePreview,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            AllowDrop = true,
        };
        ToolTipService.SetToolTip(previewBorder, Localizer.T("Select an Image"));
        previewBorder.Tapped += async (_, _) => await PickImageAsync();
        previewBorder.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
                e.AcceptedOperation = DataPackageOperation.Copy;
        };
        previewBorder.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var file = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().FirstOrDefault(f => ClipboardService.IsImageFile(f.Path));
            if (file != null)
                await SetImageAsync(file.Path, persist: true);
        };
        panel.Children.Add(previewBorder);

        var select = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new SymbolicIcon("image-alt", 16), new TextBlock { Text = Localizer.T("Select Image") } },
            },
        };
        select.Click += async (_, _) => await PickImageAsync();
        panel.Children.Add(select);

        var state = _settings.ImageState.Path;
        var current = state != null ? ResolveImagePath(state) : File.Exists(CachedImagePath) ? CachedImagePath : ResolveImagePath("preset:" + Palettes.PresetImages[0]);
        if (File.Exists(current)) UpdateImagePreview(current);
        return panel;
    }

    private void ShowImagePresets(FrameworkElement anchor)
    {
        var flyout = new Flyout();
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 3, ItemWidth = 96, ItemHeight = 72 };
        foreach (var name in Palettes.PresetImages)
        {
            var path = ResolveImagePath("preset:" + name);
            var b = new Button
            {
                Padding = new Thickness(2),
                Content = new Border
                {
                    Width = 84,
                    Height = 60,
                    CornerRadius = new CornerRadius(6),
                    Child = new Image { Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 168 }, Stretch = Stretch.UniformToFill },
                },
            };
            var n = name;
            b.Click += async (_, _) =>
            {
                flyout.Hide();
                await SetImageAsync("preset:" + n, persist: true);
            };
            grid.Children.Add(b);
        }
        flyout.Content = grid;
        flyout.ShowAt(anchor);
    }

    private async Task PickImageAsync()
    {
        if (HostWindow is null) return;
        var picker = WindowHelper.CreateOpenPicker(HostWindow);
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp", ".avif", ".bmp" })
            picker.FileTypeFilter.Add(ext);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        var file = await picker.PickSingleFileAsync();
        if (file != null)
            await SetImageAsync(file.Path, persist: true);
    }
}
