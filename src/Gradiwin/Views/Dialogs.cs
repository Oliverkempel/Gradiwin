// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Controls;
using Gradiwin.Core.Presets;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Gradiwin.Views;

/// <summary>Port of ui/dialog/confirm_close_dialog.py. Returns true = copy and close, false = close, null = cancel.</summary>
public static class ConfirmCloseDialog
{
    public static async Task<bool?> ShowAsync(XamlRoot root)
    {
        var dontAsk = new CheckBox { Content = Localizer.T("Don't Ask Again") };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Localizer.T("Close Gradia?").Replace("Gradia", "Gradiwin", StringComparison.Ordinal),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Localizer.T("Are you sure you want to exit? All unsaved changes will be lost."), TextWrapping = TextWrapping.Wrap },
                    dontAsk,
                },
            },
            PrimaryButtonText = Localizer.T("Copy"),
            SecondaryButtonText = Localizer.T("Close"),
            CloseButtonText = Localizer.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        bool? choice = result switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => null,
        };
        if (choice != null && dontAsk.IsChecked == true)
            SettingsService.Instance.ExitMethod = choice.Value ? "copy" : "none";
        return choice;
    }
}

/// <summary>Port of ui/dialog/ocr_dialog.py: recognized text with language choice and copy.</summary>
public static class OcrDialog
{
    public static async Task ShowAsync(XamlRoot root, CanvasBitmap image, bool copyAutomatically = false)
    {
        var text = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = false,
            MinHeight = 240,
            MaxHeight = 420,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        var status = new TextBlock { Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var progress = new ProgressBar { IsIndeterminate = true };
        var languages = new ComboBox { MinWidth = 200, Header = Localizer.T("Select Language") };
        var available = OcrService.AvailableLanguages.ToList();
        foreach (var l in available)
            languages.Items.Add(l.NativeName);
        var selected = OcrService.SelectedLanguage;
        languages.SelectedIndex = selected is null ? (available.Count > 0 ? 0 : -1) : available.FindIndex(l => l.LanguageTag == selected.LanguageTag);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Localizer.T("Text Extraction"),
            Content = new StackPanel { Spacing = 10, MinWidth = 460, Children = { languages, progress, text, status } },
            PrimaryButtonText = Localizer.T("Copy"),
            CloseButtonText = Localizer.T("Close"),
            DefaultButton = ContentDialogButton.Primary,
        };

        async Task Recognize()
        {
            progress.Visibility = Visibility.Visible;
            status.Text = "";
            try
            {
                var lang = languages.SelectedIndex >= 0 && languages.SelectedIndex < available.Count ? available[languages.SelectedIndex] : null;
                var result = await OcrService.RecognizeAsync(image, lang);
                text.Text = result;
                if (string.IsNullOrWhiteSpace(result))
                    status.Text = Localizer.T("No Text Found") + " — " + Localizer.T("Crop image to adjust OCR area");
                else if (copyAutomatically)
                {
                    ClipboardService.CopyText(result);
                    status.Text = Localizer.T("Text copied to clipboard");
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e.HResult != 0)
            {
                status.Text = e.Message;
            }
            finally
            {
                progress.Visibility = Visibility.Collapsed;
            }
        }

        languages.SelectionChanged += async (_, _) =>
        {
            if (languages.SelectedIndex >= 0 && languages.SelectedIndex < available.Count)
                SettingsService.Instance.TrainedData = available[languages.SelectedIndex].LanguageTag;
            await Recognize();
        };
        dialog.Opened += async (_, _) => await Recognize();
        dialog.PrimaryButtonClick += (_, _) =>
        {
            if (!string.IsNullOrEmpty(text.Text))
                ClipboardService.CopyText(text.Text);
        };
        await dialog.ShowAsync();
    }
}

/// <summary>Port of ui_parts.ShortcutsDialog.</summary>
public static class ShortcutsDialog
{
    public static async Task ShowAsync(XamlRoot root)
    {
        var sections = new (string Title, (string Keys, string Action)[] Items)[]
        {
            ("File", [
                ("Ctrl+O", "Open Image"), ("Ctrl+A", "Take a Screenshot"), ("Ctrl+V", "Paste From Clipboard"),
                ("Ctrl+P", "Source Snippets"), ("Ctrl+S", "Save File"), ("Ctrl+C", "Copy to Clipboard"),
                ("Ctrl+M", "Upload Image"), ("Ctrl+Shift+D", "Delete Taken Screenshot(s)"),
            ]),
            ("Annotations", [
                ("Ctrl+Z", "Undo"), ("Ctrl+Shift+Z / Ctrl+Y", "Redo"), ("Delete / Ctrl+X", "Erase Selected"),
            ]),
            ("Tools", ToolDefinition.All.Select(t => (string.Join(", ", t.Shortcuts.Select(s => s.ToUpperInvariant())), t.Label)).ToArray()),
            ("Image", [
                ("Ctrl+R", "Crop Image"), ("Ctrl+Shift+R", "Reset Crop"), ("Esc", "Back"), ("Ctrl+Shift+O", "Text Extraction"),
            ]),
            ("View", [
                ("Ctrl++ / Ctrl+Wheel", "Zoom In"), ("Ctrl+-", "Zoom Out"), ("Ctrl+0", "Reset Zoom"),
                ("Ctrl+Arrows / Middle-drag / Space-drag", "Pan"), ("F9", "Toggle Sidebar"),
            ]),
            ("General", [
                ("Ctrl+,", "Preferences"), ("Ctrl+?", "Keyboard Shortcuts"), ("Ctrl+W / Ctrl+Q", "Close"),
            ]),
        };

        var panel = new StackPanel { Spacing = 4, MinWidth = 440 };
        foreach (var (title, items) in sections)
        {
            panel.Children.Add(new TextBlock { Text = Localizer.T(title), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 12, 0, 4) });
            foreach (var (keys, action) in items)
            {
                var g = new Grid { Padding = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock { Text = Localizer.StripMnemonic(Localizer.T(action)) });
                var k = new Border
                {
                    Padding = new Thickness(6, 1, 6, 1),
                    CornerRadius = new CornerRadius(4),
                    Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                    Child = new TextBlock { Text = keys, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 },
                };
                Grid.SetColumn(k, 1);
                g.Children.Add(k);
                panel.Children.Add(g);
            }
        }
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Localizer.StripMnemonic(Localizer.T("_Keyboard Shortcuts")),
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            CloseButtonText = Localizer.T("Close"),
        };
        await dialog.ShowAsync();
    }
}

/// <summary>Port of ui_parts.AboutDialog.</summary>
public static class AboutDialog
{
    public static async Task ShowAsync(XamlRoot root)
    {
        var version = typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "1.14.0";
        var links = new StackPanel { Spacing = 0 };
        foreach (var (label, url) in new[]
        {
            ("Original Gradia project", "https://github.com/AlexanderVanhee/Gradia"),
            ("Report an Issue", "https://github.com/AlexanderVanhee/Gradia/issues"),
            ("D_onate to Gradia", "https://ko-fi.com/alexandervanhee"),
        })
        {
            links.Children.Add(new HyperlinkButton { Content = Localizer.StripMnemonic(Localizer.T(label)), NavigateUri = new Uri(url) });
        }
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon256.png")), Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Gradiwin", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = Localizer.F("Version {0}", version), HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
                new TextBlock
                {
                    Text = Localizer.T("Make your screenshots ready for the world.") + "\n\n" +
                           "A native Windows (WinUI 3) port of Gradia by Alexander Vanhee and contributors. " +
                           "Licensed under the GNU General Public License v3.0 or later.",
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 380,
                },
                links,
            },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Localizer.StripMnemonic(Localizer.T("_About Gradia")).Replace("Gradia", "Gradiwin", StringComparison.Ordinal),
            Content = content,
            CloseButtonText = Localizer.T("Close"),
        };
        await dialog.ShowAsync();
    }
}
