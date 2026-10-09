// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.WinUI.Controls;
using Gradiwin.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gradiwin.Views;

/// <summary>Port of ui/preferences/preferences_window.py (+ provider pages and OCR model page).</summary>
public sealed class PreferencesWindow : Window
{
    public enum Page
    {
        General,
        Upload,
        Ocr,
    }

    private static PreferencesWindow? s_instance;
    private readonly SettingsService _s = SettingsService.Instance;
    private readonly NavigationView _nav;
    private readonly ScrollViewer _content = new() { Padding = new Thickness(32, 8, 32, 32) };

    public static PreferencesWindow Show(Page page)
    {
        if (s_instance is null)
        {
            s_instance = new PreferencesWindow();
            s_instance.Closed += (_, _) => s_instance = null;
            App.Current.Track(s_instance);
        }
        s_instance.Navigate(page);
        s_instance.Activate();
        WindowHelper.BringToFront(s_instance);
        return s_instance;
    }

    private PreferencesWindow()
    {
        Title = Localizer.StripMnemonic(Localizer.T("_Preferences"));
        SystemBackdrop = new MicaBackdrop();
        WindowHelper.SetIcon(this);
        WindowHelper.Resize(this, 880, 680);
        ExtendsContentIntoTitleBar = true;

        _nav = new NavigationView
        {
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            OpenPaneLength = 220,
            IsPaneToggleButtonVisible = false,
            Content = _content,
        };
        _nav.MenuItems.Add(new NavigationViewItem { Content = Localizer.T("General"), Icon = new FontIcon { Glyph = "" }, Tag = Page.General });
        _nav.MenuItems.Add(new NavigationViewItem { Content = Localizer.T("Upload"), Icon = new FontIcon { Glyph = "" }, Tag = Page.Upload });
        _nav.MenuItems.Add(new NavigationViewItem { Content = Localizer.T("Text Extraction"), Icon = new FontIcon { Glyph = "" }, Tag = Page.Ocr });
        _nav.SelectionChanged += (_, e) =>
        {
            if (e.SelectedItem is NavigationViewItem { Tag: Page p })
                ShowPage(p);
        };

        var titleBar = new Grid { Height = 44, Padding = new Thickness(16, 0, 0, 0) };
        titleBar.Children.Add(new TextBlock { Text = Title, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center });
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(titleBar);
        Grid.SetRow(_nav, 1);
        root.Children.Add(_nav);
        Content = root;
        SetTitleBar(titleBar);
    }

    public void Navigate(Page page) =>
        _nav.SelectedItem = _nav.MenuItems.OfType<NavigationViewItem>().First(i => (Page)i.Tag == page);

    private void ShowPage(Page page)
    {
        _content.Content = page switch
        {
            Page.Upload => BuildUploadPage(),
            Page.Ocr => BuildOcrPage(),
            _ => BuildGeneralPage(),
        };
    }

    private static TextBlock GroupHeader(string text) => new()
    {
        Text = Localizer.T(text),
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        Margin = new Thickness(2, 24, 0, 8),
    };

    private static SettingsCard Card(string header, string? description, object content, string? glyph = null) => new()
    {
        Header = Localizer.StripMnemonic(Localizer.T(header)),
        Description = description is null ? "" : Localizer.T(description),
        Content = content,
        HeaderIcon = glyph is null ? null! : new FontIcon { Glyph = glyph },
    };

    // ---------------- General ----------------

    private UIElement BuildGeneralPage()
    {
        var page = new StackPanel { Spacing = 4, MaxWidth = 900 };

        page.Children.Add(GroupHeader("Screenshots"));
        var folderText = new TextBlock
        {
            Text = RecentScreenshots.ResolveFolder(),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 260,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        var choose = new Button { Content = Localizer.T("Choose…") };
        choose.Click += async (_, _) =>
        {
            var picker = WindowHelper.InitPicker(new Windows.Storage.Pickers.FolderPicker(), this);
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _s.ScreenshotFolder = folder.Path;
                folderText.Text = folder.Path;
            }
        };
        var reset = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
        ToolTipService.SetToolTip(reset, Localizer.T("Reset"));
        reset.Click += (_, _) =>
        {
            _s.ScreenshotFolder = "";
            folderText.Text = RecentScreenshots.ResolveFolder();
        };
        page.Children.Add(Card("Recent Screenshots Folder", "Where screenshots are saved and the welcome page looks for recent ones",
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { folderText, choose, reset } }, ""));

        page.Children.Add(BuildPrintScreenSection());

        page.Children.Add(GroupHeader("Export"));
        var format = new ComboBox { MinWidth = 160 };
        foreach (var f in ImageEncoder.Formats)
            format.Items.Add(f.ShortName);
        format.SelectedIndex = Math.Max(0, ImageEncoder.Formats.ToList().FindIndex(f => f.Key == _s.ExportFormat));
        format.SelectionChanged += (_, _) => _s.ExportFormat = ImageEncoder.Formats[format.SelectedIndex].Key;
        page.Children.Add(Card("Image Format", "Default format in the save dialog", format, ""));

        var compress = new ToggleSwitch { IsOn = _s.ExportCompress };
        compress.Toggled += (_, _) => _s.ExportCompress = compress.IsOn;
        page.Children.Add(Card("Compress Images", "Use quality 90 for JPEG and lossy WebP; off saves at maximum quality", compress, ""));

        page.Children.Add(GroupHeader("Behavior"));
        var exit = new ComboBox { MinWidth = 200 };
        var methods = new[] { ("confirm", "Ask"), ("copy", "Copy and Close"), ("none", "Close Instantly") };
        foreach (var (_, label) in methods)
            exit.Items.Add(Localizer.T(label));
        exit.SelectedIndex = Math.Max(0, Array.FindIndex(methods, m => m.Item1 == _s.ExitMethod));
        exit.SelectionChanged += (_, _) => _s.ExitMethod = methods[exit.SelectedIndex].Item1;
        page.Children.Add(Card("When Exiting", "What happens to the edited image when closing a window", exit, ""));

        var overwrite = new ToggleSwitch { IsOn = _s.OverwriteScreenshot };
        overwrite.Toggled += (_, _) => _s.OverwriteScreenshot = overwrite.IsOn;
        page.Children.Add(Card("Overwrite Screenshot", "Replace the original screenshot file with the edited version on close", overwrite, ""));

        var trash = new ToggleSwitch { IsOn = _s.TrashScreenshotsOnClose };
        trash.Toggled += (_, _) => _s.TrashScreenshotsOnClose = trash.IsOn;
        page.Children.Add(Card("Delete Screenshots on Close", "Move screenshots taken in the session to the Recycle Bin when closing", trash, ""));

        return page;
    }

    /// <summary>
    /// Print Screen integration (replaces Gradia's screenshot_guide_page): Gradiwin claims the
    /// Print Screen key while it runs in the notification area, optionally starting with Windows.
    /// </summary>
    private UIElement BuildPrintScreenSection()
    {
        var panel = new StackPanel { Spacing = 4 };

        var status = new InfoBar { IsClosable = false, Margin = new Thickness(0, 0, 0, 4) };
        var openKeyboard = new Button { Content = Localizer.T("Open Keyboard Settings") };
        openKeyboard.Click += (_, _) => _ = WindowHelper.OpenUriAsync("ms-settings:easeofaccess-keyboard");
        var retry = new Button { Content = Localizer.T("Try Again") };
        retry.Click += (_, _) => HotkeyService.Apply();
        // Built once: an element can only have one parent, so the status bar reuses this panel.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8), Children = { openKeyboard, retry } };

        var hotkey = new ToggleSwitch { IsOn = _s.PrintScreenHotkey };
        var startup = new ToggleSwitch { IsEnabled = false };

        void UpdateStatus()
        {
            try
            {
                UpdateStatusCore();
            }
            catch (Exception e)
            {
                Log.Error("Updating the Print Screen status failed", e);
            }
        }

        void UpdateStatusCore()
        {
            // With the keyboard hook active, Windows' Snipping Tool binding no longer gets the key.
            bool windowsBinding = HotkeyService.IsWindowsPrintScreenBindingEnabled() && !HotkeyService.IsHookActive;
            switch (HotkeyService.CurrentStatus)
            {
                case HotkeyService.Status.Active when windowsBinding:
                    status.Severity = InfoBarSeverity.Warning;
                    status.Title = Localizer.T("Windows may still open Snipping Tool");
                    status.Message = Localizer.T("Turn off \"Use the Print screen key to open screen capture\" in Windows keyboard settings so Print Screen always opens Gradiwin.");
                    retry.Visibility = Visibility.Collapsed;
                    status.Content = actions;
                    break;
                case HotkeyService.Status.Active:
                    status.Severity = InfoBarSeverity.Success;
                    status.Title = Localizer.T("Print Screen opens Gradiwin");
                    status.Message = Localizer.T("PrtScn: select an area or window · Ctrl+PrtScn: all screens. Gradiwin stays in the notification area after you close its windows.");
                    status.Content = null;
                    break;
                case HotkeyService.Status.KeyTaken:
                    status.Severity = InfoBarSeverity.Error;
                    status.Title = Localizer.T("Print Screen is used by another program");
                    status.Message = windowsBinding
                        ? Localizer.T("Windows uses Print Screen for Snipping Tool. Turn off \"Use the Print screen key to open screen capture\" in keyboard settings, then try again.")
                        : Localizer.T("Another app (for example OneDrive, ShareX or Greenshot) has claimed Print Screen. Turn it off there, then try again.");
                    retry.Visibility = Visibility.Visible;
                    status.Content = actions;
                    break;
            }
            status.IsOpen = HotkeyService.CurrentStatus != HotkeyService.Status.Off;
        }

        async Task UpdateStartupAsync()
        {
            var state = await StartupService.GetStateAsync();
            startup.IsEnabled = _s.PrintScreenHotkey && state is not (null or Windows.ApplicationModel.StartupTaskState.DisabledByPolicy or Windows.ApplicationModel.StartupTaskState.DisabledByUser);
            startup.IsOn = state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            if (state == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
                ToolTipService.SetToolTip(startup, Localizer.T("Disabled in Task Manager › Startup apps"));
        }

        hotkey.Toggled += async (_, _) =>
        {
            _s.PrintScreenHotkey = hotkey.IsOn;
            HotkeyService.Apply();
            if (!hotkey.IsOn)
                await StartupService.SetEnabledAsync(false);
            UpdateStatus();
            await UpdateStartupAsync();
        };
        startup.Toggled += async (_, _) =>
        {
            if (!startup.IsEnabled) return;
            await StartupService.SetEnabledAsync(startup.IsOn);
            await UpdateStartupAsync();
        };

        void OnStatusChanged() => DispatcherQueue.TryEnqueue(UpdateStatus);
        HotkeyService.StatusChanged += OnStatusChanged;
        Closed += (_, _) => HotkeyService.StatusChanged -= OnStatusChanged;
        Activated += (_, _) => UpdateStatus(); // refresh after returning from Windows settings

        panel.Children.Add(Card("Use Print Screen for Gradiwin", "Take screenshots with the Print Screen key instead of Snipping Tool", hotkey, ""));
        panel.Children.Add(status);
        panel.Children.Add(Card("Start with Windows", "Keep Gradiwin ready in the notification area after signing in", startup, ""));
        UpdateStatus();
        _ = UpdateStartupAsync();

        // Command-line equivalents for custom shortcuts (Gradia's screenshot guide).
        var steps = new StackPanel { Spacing = 8, Padding = new Thickness(0, 4, 0, 4) };
        steps.Children.Add(new TextBlock
        {
            Text = Localizer.T("These commands can also be used as the target of a desktop shortcut with its own \"Shortcut key\"."),
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var (label, cmd) in new[]
        {
            ("Interactive screenshot", "gradia --screenshot=INTERACTIVE"),
            ("All screens", "gradia --screenshot=FULL"),
            ("All screens after 3 seconds", "gradia --screenshot=FULL --delay=3000"),
        })
        {
            var copy = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
            ToolTipService.SetToolTip(copy, Localizer.T("Copy"));
            var c = cmd;
            copy.Click += (_, _) => ClipboardService.CopyText(c);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = Localizer.T(label), Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] },
                    new TextBlock { Text = cmd, FontFamily = new FontFamily("Cascadia Mono, Consolas"), IsTextSelectionEnabled = true },
                },
            });
            Grid.SetColumn(copy, 1);
            row.Children.Add(copy);
            steps.Children.Add(row);
        }
        var expander = new SettingsExpander
        {
            Header = Localizer.T("Command Line"),
            Description = Localizer.T("Start a screenshot from scripts or custom shortcuts"),
            HeaderIcon = new FontIcon { Glyph = "" },
        };
        expander.Items.Add(new SettingsCard { Content = steps, HorizontalContentAlignment = HorizontalAlignment.Stretch, ContentAlignment = ContentAlignment.Vertical });
        panel.Children.Add(expander);
        return panel;
    }

    // ---------------- Upload (provider_selection_window.py) ----------------

    private UIElement BuildUploadPage()
    {
        var page = new StackPanel { Spacing = 4, MaxWidth = 900 };
        page.Children.Add(GroupHeader("Upload Provider"));
        page.Children.Add(new TextBlock
        {
            Text = Localizer.T("Choose where Ctrl+M and the share button upload images. The resulting link is copied to the clipboard."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(2, 0, 0, 8),
        });

        var radios = new List<RadioButton>();
        var details = new StackPanel { Spacing = 4 };

        void Select(string id)
        {
            _s.ProviderName = id;
            foreach (var r in radios) r.IsChecked = (string)r.Tag == id;
            ShowDetails(details, id);
        }

        foreach (var provider in UploadService.Providers)
        {
            var radio = new RadioButton { GroupName = "provider", Tag = provider.Id, IsChecked = _s.ProviderName == provider.Id, MinWidth = 0 };
            var id = provider.Id;
            radio.Checked += (_, _) => { if (_s.ProviderName != id) Select(id); };
            radios.Add(radio);
            var card = Card(provider.Name, provider.Description, radio);
            card.IsClickEnabled = true;
            card.Click += (_, _) => Select(id);
            page.Children.Add(card);
        }
        var customRadio = new RadioButton { GroupName = "provider", Tag = UploadService.CustomProviderId, IsChecked = _s.ProviderName == UploadService.CustomProviderId, MinWidth = 0 };
        customRadio.Checked += (_, _) => { if (_s.ProviderName != UploadService.CustomProviderId) Select(UploadService.CustomProviderId); };
        radios.Add(customRadio);
        var customCard = Card("Custom", "Run your own command; $1 is replaced with the image path", customRadio, "");
        customCard.IsClickEnabled = true;
        customCard.Click += (_, _) => Select(UploadService.CustomProviderId);
        page.Children.Add(customCard);

        page.Children.Add(details);
        ShowDetails(details, _s.ProviderName);

        page.Children.Add(GroupHeader("Behavior"));
        var confirm = new ToggleSwitch { IsOn = _s.ShowExportConfirmDialog };
        confirm.Toggled += (_, _) => _s.ShowExportConfirmDialog = confirm.IsOn;
        page.Children.Add(Card("Confirm Before Uploading", null, confirm, ""));
        return page;
    }

    private void ShowDetails(StackPanel details, string id)
    {
        details.Children.Clear();
        if (id == UploadService.CustomProviderId)
        {
            details.Children.Add(GroupHeader("Custom Command"));
            var box = new TextBox
            {
                Text = _s.CustomExportCommand,
                AcceptsReturn = false,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                PlaceholderText = "curl.exe -s -F \"file=@$1\" https://example.com/upload",
            };
            box.TextChanged += (_, _) => _s.CustomExportCommand = box.Text;
            details.Children.Add(box);
            details.Children.Add(new TextBlock
            {
                Text = Localizer.T("The command runs in PowerShell. $1 is replaced with the quoted path of a temporary PNG; whatever the command prints is copied to the clipboard."),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            return;
        }

        var provider = UploadService.Providers.FirstOrDefault(p => p.Id == id);
        if (provider is null) return;
        details.Children.Add(GroupHeader(provider.Name));
        details.Children.Add(new TextBlock { Text = provider.Details, TextWrapping = TextWrapping.Wrap });
        foreach (var f in provider.Features)
        {
            var (glyph, brush) = f.Type switch
            {
                "positive" => ("", "SystemFillColorSuccessBrush"),
                "negative" => ("", "SystemFillColorCriticalBrush"),
                _ => ("", "TextFillColorSecondaryBrush"),
            };
            details.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 12, Foreground = (Brush)Application.Current.Resources[brush] },
                    new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap },
                },
            });
        }
        var links = new StackPanel { Orientation = Orientation.Horizontal };
        if (provider.HomepageUrl.Length > 0)
            links.Children.Add(new HyperlinkButton { Content = Localizer.T("Website"), NavigateUri = new Uri(provider.HomepageUrl) });
        if (provider.TosUrl.Length > 0)
            links.Children.Add(new HyperlinkButton { Content = Localizer.T("Terms of Service"), NavigateUri = new Uri(provider.TosUrl) });
        details.Children.Add(links);

        if (id == "imgur")
        {
            var clientId = new TextBox { Text = _s.ImgurClientId, PlaceholderText = Localizer.T("Client ID"), MinWidth = 260 };
            clientId.TextChanged += (_, _) => _s.ImgurClientId = clientId.Text.Trim();
            details.Children.Add(Card("Imgur Client ID", "Register an application at api.imgur.com/oauth2/addclient (anonymous usage)", clientId, ""));
        }
    }

    // ---------------- OCR (ocr_model_page.py) ----------------

    private UIElement BuildOcrPage()
    {
        var page = new StackPanel { Spacing = 4, MaxWidth = 900 };
        page.Children.Add(GroupHeader("Text Extraction"));
        page.Children.Add(new TextBlock
        {
            Text = Localizer.T("Text recognition uses the OCR engine built into Windows. Languages come from installed Windows language packs; add more in Settings › Time & language › Language & region."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(2, 0, 0, 8),
        });

        var languages = OcrService.AvailableLanguages;
        var combo = new ComboBox { MinWidth = 220 };
        combo.Items.Add(Localizer.T("Automatic (user profile languages)"));
        foreach (var l in languages)
            combo.Items.Add($"{l.NativeName} ({l.LanguageTag})");
        var selected = OcrService.SelectedLanguage;
        combo.SelectedIndex = selected is null ? 0 : 1 + languages.ToList().FindIndex(l => l.LanguageTag == selected.LanguageTag);
        combo.SelectionChanged += (_, _) =>
            _s.TrainedData = combo.SelectedIndex <= 0 ? "" : languages[combo.SelectedIndex - 1].LanguageTag;
        page.Children.Add(Card("Recognition Language", null, combo, ""));

        var open = new Button { Content = Localizer.T("Open Language Settings") };
        open.Click += (_, _) => _ = WindowHelper.OpenUriAsync("ms-settings:regionlanguage");
        page.Children.Add(Card("Add Languages", "Install a language with the optional \"Optical character recognition\" feature", open, ""));

        page.Children.Add(GroupHeader("Installed OCR Languages"));
        foreach (var l in languages)
            page.Children.Add(new SettingsCard { Header = l.NativeName, Description = l.DisplayName + " · " + l.LanguageTag, HeaderIcon = new FontIcon { Glyph = "" } });
        if (languages.Count == 0)
            page.Children.Add(new TextBlock { Text = Localizer.T("No OCR languages are installed."), Margin = new Thickness(2) });
        return page;
    }
}
