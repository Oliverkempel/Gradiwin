// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Controls;
using Gradiwin.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Gradiwin.Views;

/// <summary>Port of ui/welcome_page.py with the recent screenshots picker (ui/recent_picker.py).</summary>
public sealed partial class WelcomeView : UserControl
{
    private readonly StackPanel _recentPanel = new() { Spacing = 10, Margin = new Thickness(0, 28, 0, 0) };

    public event Action? OpenRequested;
    public event Action? ScreenshotRequested;
    public event Action? SourceSnippetRequested;
    public event Action<string>? FileChosen;

    public Window? HostWindow { get; set; }

    public WelcomeView()
    {
        var icon = new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon256.png")),
            Width = 128,
            Height = 128,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var title = new TextBlock
        {
            Text = Localizer.T("Enhance an Image"),
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var subtitle = new TextBlock
        {
            Text = Localizer.T("Drag and drop one here"),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var screenshot = BigButton(Localizer.T("Take a Screenshot…"), new SymbolicIcon("screenshooter", 16), accent: true, "Ctrl+A");
        screenshot.Click += (_, _) => ScreenshotRequested?.Invoke();
        var open = BigButton(Localizer.T("Open Image…"), new FontIcon { Glyph = "", FontSize = 16 }, accent: false, "Ctrl+O");
        open.Click += (_, _) => OpenRequested?.Invoke();
        var snippet = BigButton(Localizer.StripMnemonic(Localizer.T("_Source Snippets")), new SymbolicIcon("code", 16), accent: false, "Ctrl+P");
        snippet.Click += (_, _) => SourceSnippetRequested?.Invoke();

        var buttons = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0), Width = 260 };
        buttons.Children.Add(screenshot);
        buttons.Children.Add(open);
        buttons.Children.Add(snippet);

        var paste = new TextBlock
        {
            Text = Localizer.T("or paste an image with Ctrl+V"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(24) };
        content.Children.Add(icon);
        content.Children.Add(title);
        content.Children.Add(subtitle);
        content.Children.Add(buttons);
        content.Children.Add(paste);
        content.Children.Add(_recentPanel);

        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => RefreshRecent();
    }

    private static Button BigButton(string text, UIElement icon, bool accent, string shortcut)
    {
        var b = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 10, 16, 10),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { icon, new TextBlock { Text = text } },
            },
        };
        if (accent)
            b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        ToolTipService.SetToolTip(b, shortcut);
        return b;
    }

    /// <summary>Shows the newest screenshots from the screenshot folder (RecentImageGetter).</summary>
    public void RefreshRecent()
    {
        _recentPanel.Children.Clear();
        var files = RecentScreenshots.Get(6);
        var folder = RecentScreenshots.ResolveFolder();
        if (files.Count == 0)
        {
            if (!string.IsNullOrEmpty(SettingsService.Instance.ScreenshotFolder) && !Directory.Exists(SettingsService.Instance.ScreenshotFolder))
            {
                _recentPanel.Children.Add(new TextBlock
                {
                    Text = Localizer.T("Screenshot Folder Not Found") + " — " + Localizer.T("Please set it in the Preferences menu"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
            }
            return;
        }

        _recentPanel.Children.Add(new TextBlock
        {
            Text = Localizer.F("Recent screenshots in {0}", Path.GetFileName(folder)),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var f in files)
        {
            var thumb = new Border
            {
                Width = 120,
                Height = 80,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Child = new Image
                {
                    Source = new BitmapImage(new Uri(f.FullName)) { DecodePixelWidth = 240 },
                    Stretch = Stretch.UniformToFill,
                },
            };
            var b = new Button { Content = thumb, Padding = new Thickness(2), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            ToolTipService.SetToolTip(b, f.Name);
            var path = f.FullName;
            b.Click += (_, _) => FileChosen?.Invoke(path);
            row.Children.Add(b);
        }
        _recentPanel.Children.Add(new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
    }
}
