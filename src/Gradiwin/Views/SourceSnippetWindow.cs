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
    private readonly ComboBox _language = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _theme = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
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
        WindowHelper.Resize(this, 1200, 760);

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

        // Every option has a visible label (the top-row toolbar had several unlabelled controls).
        _language.Header = Localizer.T("Language");
        _theme.Header = Localizer.T("Theme");
        _frame = new ToggleSwitch { Header = Localizer.T("Window frame"), IsOn = _s.SourceSnippetShowFrame };
        _frame.Toggled += (_, _) => { _s.SourceSnippetShowFrame = _frame.IsOn; _title!.IsEnabled = _frame.IsOn; Rerender(); };
        _title = new TextBox
        {
            Header = Localizer.T("Window title"),
            Text = _s.SourceSnippetTitle,
            PlaceholderText = Localizer.T("Title"),
            IsEnabled = _frame.IsOn,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _title.TextChanged += (_, _) => { _s.SourceSnippetTitle = _title.Text; Rerender(); };
        _lineNumbers = new ToggleSwitch { Header = Localizer.T("Line numbers"), IsOn = _s.SourceSnippetShowLineNumbers };
        _lineNumbers.Toggled += (_, _) => { _s.SourceSnippetShowLineNumbers = _lineNumbers.IsOn; Rerender(); };
        _width = new Slider { Minimum = 320, Maximum = 1400, Value = _s.SourceSnippetWidth, StepFrequency = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        string WidthLabel() => Localizer.F("Width: {0} px", (int)_width.Value);
        _width.Header = WidthLabel();
        _width.ValueChanged += (_, _) => { _s.SourceSnippetWidth = _width.Value; _width.Header = WidthLabel(); Rerender(); };

        _preview.Draw += OnPreviewDraw;
        _preview.CreateResources += (_, _) => Rerender();

        var create = new Button
        {
            Content = Localizer.T("Create Image"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        create.Click += async (_, _) => await CreateAsync();

        // Options sidebar, grouped like the main window's sidebar.
        TextBlock Section(string text) => new()
        {
            Text = Localizer.T(text),
            Style = (Style)Application.Current.Resources["SidebarHeaderStyle"],
            Margin = new Thickness(2, 8, 0, 0),
        };
        var options = new StackPanel { Spacing = 12, Padding = new Thickness(0, 0, 12, 0) };
        options.Children.Add(Section("Highlighting"));
        options.Children.Add(_language);
        options.Children.Add(_theme);
        options.Children.Add(Section("Window"));
        options.Children.Add(_frame);
        options.Children.Add(_title);
        options.Children.Add(Section("Layout"));
        options.Children.Add(_lineNumbers);
        options.Children.Add(_width);
        var optionsHost = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        // Code and preview columns, each with a caption so it's clear which side is which.
        FrameworkElement Captioned(string caption, FrameworkElement content)
        {
            var g = new Grid { RowSpacing = 6 };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(Section(caption));
            Grid.SetRow(content, 1);
            g.Children.Add(content);
            return g;
        }
        var previewHost = new Border
        {
            Child = _preview,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
        };

        var body = new Grid { ColumnSpacing = 16, Padding = new Thickness(16, 0, 16, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        body.Children.Add(optionsHost);
        var codeColumn = Captioned("Code", _code);
        Grid.SetColumn(codeColumn, 1);
        body.Children.Add(codeColumn);
        var previewColumn = Captioned("Preview", previewHost);
        Grid.SetColumn(previewColumn, 2);
        body.Children.Add(previewColumn);

        var footer = new Grid { Padding = new Thickness(16, 12, 16, 16), Children = { create } };

        // Same integrated title bar as the main window.
        var titleBar = WindowHelper.CreateTitleBar(this, Title);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(titleBar);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Grid.SetRow(footer, 2);
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
