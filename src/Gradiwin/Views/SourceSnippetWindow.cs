// SPDX-License-Identifier: GPL-3.0-or-later
using ColorCode;
using Gradiwin.Rendering;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gradiwin.Views;

/// <summary>Port of ui/image_creation/source_image_generator.py (Ctrl+P "Source Snippets").</summary>
public sealed class SourceSnippetWindow : Window
{
    private readonly SettingsService _s = SettingsService.Instance;
    private readonly Func<string, Task> _onCreated;
    private readonly TextBox _code;
    private readonly ComboBox _language = new() { MinWidth = 150, Header = null };
    private readonly ComboBox _theme = new() { MinWidth = 110 };
    private readonly ToggleSwitch _frame;
    private readonly TextBox _title;
    private readonly ToggleSwitch _lineNumbers;
    private readonly Slider _width;
    private readonly CanvasControl _preview = new();
    private readonly IReadOnlyList<ILanguage> _languages = SourceSnippetRenderer.Languages;
    private CanvasRenderTarget? _rendered;

    public SourceSnippetWindow(Func<string, Task> onCreated)
    {
        _onCreated = onCreated;
        Title = Localizer.StripMnemonic(Localizer.T("_Source Snippets"));
        SystemBackdrop = new MicaBackdrop();
        WindowHelper.SetIcon(this);
        WindowHelper.Resize(this, 1100, 720);

        _code = new TextBox
        {
            AcceptsReturn = true, // must precede Text, or the text is cut at the first newline
            TextWrapping = TextWrapping.NoWrap,
            Text = ToTextBox(_s.SourceSnippetCodeText),
            FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas"),
            IsSpellCheckEnabled = false,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_code, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_code, ScrollBarVisibility.Auto);
        _code.TextChanged += (_, _) => { _s.SourceSnippetCodeText = CodeText; Rerender(); };

        foreach (var l in _languages)
            _language.Items.Add(l.Name);
        var current = SourceSnippetRenderer.FindLanguage(_s.SourceSnippetLanguage);
        _language.SelectedIndex = current is null ? -1 : _languages.ToList().FindIndex(l => l.Id == current.Id);
        _language.SelectionChanged += (_, _) =>
        {
            if (_language.SelectedIndex >= 0) _s.SourceSnippetLanguage = _languages[_language.SelectedIndex].Id;
            Rerender();
        };

        foreach (var t in SourceSnippetRenderer.Themes)
            _theme.Items.Add(Localizer.T(t.Name));
        _theme.SelectedIndex = Math.Max(0, SourceSnippetRenderer.Themes.ToList().FindIndex(t => t.Name == _s.SourceSnippetStyleScheme));
        _theme.SelectionChanged += (_, _) => { _s.SourceSnippetStyleScheme = SourceSnippetRenderer.Themes[_theme.SelectedIndex].Name; Rerender(); };

        _frame = new ToggleSwitch { IsOn = _s.SourceSnippetShowFrame, OnContent = Localizer.T("Window Frame"), OffContent = Localizer.T("Window Frame") };
        _frame.Toggled += (_, _) => { _s.SourceSnippetShowFrame = _frame.IsOn; _title!.IsEnabled = _frame.IsOn; Rerender(); };
        _title = new TextBox { Text = _s.SourceSnippetTitle, MinWidth = 160, PlaceholderText = Localizer.T("Title"), IsEnabled = _frame.IsOn };
        _title.TextChanged += (_, _) => { _s.SourceSnippetTitle = _title.Text; Rerender(); };
        _lineNumbers = new ToggleSwitch { IsOn = _s.SourceSnippetShowLineNumbers, OnContent = Localizer.T("Line Numbers"), OffContent = Localizer.T("Line Numbers") };
        _lineNumbers.Toggled += (_, _) => { _s.SourceSnippetShowLineNumbers = _lineNumbers.IsOn; Rerender(); };
        _width = new Slider { Minimum = 320, Maximum = 1400, Value = _s.SourceSnippetWidth, Width = 160, StepFrequency = 10, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(_width, Localizer.T("Width"));
        _width.ValueChanged += (_, _) => { _s.SourceSnippetWidth = _width.Value; Rerender(); };

        _preview.Draw += OnPreviewDraw;
        _preview.CreateResources += (_, _) => Rerender();

        var create = new Button { Content = Localizer.T("Create Image"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        create.Click += async (_, _) => await CreateAsync();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(16, 10, 16, 10) };
        toolbar.Children.Add(_language);
        toolbar.Children.Add(_theme);
        toolbar.Children.Add(_frame);
        toolbar.Children.Add(_title);
        toolbar.Children.Add(_lineNumbers);
        toolbar.Children.Add(_width);
        // The row is as tall as its tallest item (the toggle switches); without centering, the
        // title TextBox stretches to that height and looks taller than the ComboBoxes.
        foreach (var child in toolbar.Children.OfType<FrameworkElement>())
            child.VerticalAlignment = VerticalAlignment.Center;

        var body = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 0, 16, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        body.Children.Add(_code);
        var previewHost = new Border
        {
            Child = _preview,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
        };
        Grid.SetColumn(previewHost, 1);
        body.Children.Add(previewHost);

        var footer = new Grid { Padding = new Thickness(16, 12, 16, 16) };
        create.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(create);

        // Same integrated title bar as the main window.
        var titleBar = WindowHelper.CreateTitleBar(this, Title);
        var toolbarHost = new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(titleBar);
        Grid.SetRow(toolbarHost, 1);
        root.Children.Add(toolbarHost);
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Content = root;

        Closed += (_, _) =>
        {
            _preview.RemoveFromVisualTree();
            _rendered?.Dispose();
        };
    }

    /// <summary>WinUI's TextBox separates lines with CR; store and render with LF.</summary>
    private string CodeText => _code.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string ToTextBox(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\n', '\r');

    private SnippetOptions Options => new(
        CodeText,
        _language.SelectedIndex >= 0 ? _languages[_language.SelectedIndex] : null,
        SourceSnippetRenderer.Themes[Math.Max(0, _theme.SelectedIndex)],
        _frame.IsOn,
        _title.Text,
        _lineNumbers.IsOn,
        (float)_width.Value);

    private void Rerender()
    {
        if (!_preview.ReadyToDraw) return;
        try
        {
            var r = SourceSnippetRenderer.Render(_preview, Options);
            _rendered?.Dispose();
            _rendered = r;
        }
        catch (Exception e) when (e.HResult != 0)
        {
            System.Diagnostics.Debug.WriteLine(e);
        }
        _preview.Invalidate();
    }

    private void OnPreviewDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_rendered is null) return;
        var size = _rendered.Size;
        double vw = sender.ActualWidth - 24, vh = sender.ActualHeight - 24;
        double f = Math.Min(1, Math.Min(vw / size.Width, vh / size.Height));
        double w = size.Width * f, h = size.Height * f;
        args.DrawingSession.DrawImage(_rendered, new Windows.Foundation.Rect((sender.ActualWidth - w) / 2, (sender.ActualHeight - h) / 2, w, h),
            new Windows.Foundation.Rect(0, 0, size.Width, size.Height), 1f, CanvasImageInterpolation.HighQualityCubic);
    }

    private async Task CreateAsync()
    {
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            using var image = SourceSnippetRenderer.Render(device, Options);
            var path = Path.Combine(Path.GetTempPath(), "Gradiwin", $"source-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await image.SaveAsync(path, CanvasBitmapFileFormat.Png);
            await _onCreated(path);
            Close();
        }
        catch (Exception e) when (e is IOException || e.HResult != 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = Localizer.T("Export Failed"),
                Content = e.Message,
                CloseButtonText = Localizer.T("Close"),
            };
            await dialog.ShowAsync();
        }
    }
}
