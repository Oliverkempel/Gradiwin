// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Controls;
using Gradiwin.Core;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;
using Gradiwin.Core.Presets;
using Gradiwin.Rendering;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Gradiwin.Views;

/// <summary>Port of ui/window.py GradiaMainWindow.</summary>
public sealed partial class MainWindow : Window
{
    private const double CompactBreakpoint = 695;

    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly SessionFiles _session = new();
    private readonly ToolsPanel _tools = new();
    private readonly BackgroundPanel _background = new();
    private readonly ImageOptionsPanel _imageOptions = new();
    private readonly TextBlock _fileName = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _fileLocation = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _imageSize = new();
    private readonly Button _openFolderButton = new() { Content = new FontIcon { Glyph = "", FontSize = 14 }, Padding = new Thickness(8), IsEnabled = false };

    private LoadedImage? _image;
    private CanvasRenderTarget? _processed;
    private bool _reprocessQueued;
    private bool _imageReady;
    private bool _showCloseConfirmation;
    private bool _closeConfirmed;
    private bool _isCompact;
    private CropRect _cropBackup = CropRect.Full;
    private double _cropAspect;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Gradiwin";
        WindowHelper.SetIcon(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        WindowHelper.Resize(this, 975, 675);
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)(360 * WindowHelper.GetScale(this));
            p.PreferredMinimumHeight = (int)(400 * WindowHelper.GetScale(this));
        }

        BuildSidebar();
        WireCanvas();
        BuildCropAspectMenu();

        Welcome.HostWindow = this;
        Welcome.OpenRequested += () => _ = OpenFileDialogAsync();
        Welcome.ScreenshotRequested += () => _ = TakeScreenshotAsync(ScreenshotMode.Interactive);
        Welcome.SourceSnippetRequested += OpenSourceSnippet;
        Welcome.FileChosen += path => _ = LoadFileAsync(path, ImageOrigin.FileDialog);

        Root.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), true);
        Root.SizeChanged += (_, _) => UpdateCompact();
        Root.Loaded += (_, _) => UpdateCaptionInset();
        AppWindow.Closing += OnClosing;
        Closed += (_, _) =>
        {
            _image?.Dispose();
            _processed?.Dispose();
            _session.Dispose();
        };
        UploadService_Updated();
    }

    public bool HasImage => _image is not null;

    // ===================================================================================
    // Sidebar
    // ===================================================================================

    private void BuildSidebar()
    {
        _background.HostWindow = this;
        _tools.ToolChanged += mode => Canvas.SetMode(mode);
        _tools.OptionsChanged += () => Canvas.Invalidate();
        _background.Changed += _ =>
        {
            _imageOptions.SetBackgroundEnabled(_background.Mode != BackgroundMode.None);
            ScheduleReprocess();
        };
        _imageOptions.Changed += ScheduleReprocess;
        _imageOptions.SetBackgroundEnabled(_background.Mode != BackgroundMode.None);

        SidebarStack.Children.Add(_tools);
        SidebarStack.Children.Add(_background);
        SidebarStack.Children.Add(_imageOptions);

        // "Current File" group.
        SidebarStack.Children.Add(new TextBlock { Text = Localizer.T("Current File"), Style = (Style)Application.Current.Resources["SidebarHeaderStyle"] });
        _fileName.Text = Localizer.T("N/A");
        _fileLocation.Text = Localizer.T("N/A");
        _imageSize.Text = Localizer.T("N/A");
        ToolTipService.SetToolTip(_openFolderButton, Localizer.T("Open Folder"));
        _openFolderButton.Click += (_, _) => WindowHelper.OpenFolder(_image?.FolderPath, _image?.ImagePath);
        SidebarStack.Children.Add(PropertyRow(Localizer.T("Name"), _fileName, null));
        SidebarStack.Children.Add(PropertyRow(Localizer.StripMnemonic(Localizer.T("_Location")), _fileLocation, _openFolderButton));
        SidebarStack.Children.Add(PropertyRow(Localizer.T("Modified image size"), _imageSize, null));
    }

    private static Grid PropertyRow(string title, TextBlock value, FrameworkElement? suffix)
    {
        var g = new Grid
        {
            Padding = new Thickness(12, 8, 8, 8),
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Margin = new Thickness(0, 0, 0, 4),
        };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        value.Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
        var stack = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
                value,
            },
        };
        g.Children.Add(stack);
        if (suffix != null)
        {
            suffix.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(suffix, 1);
            g.Children.Add(suffix);
        }
        return g;
    }

    private void UpdateFileInfo()
    {
        if (_image is null) return;
        _fileName.Text = _image.ProperName();
        _fileLocation.Text = _image.ProperFolder;
        _openFolderButton.IsEnabled = _image.HasProperName;
        TitleText.Text = $"{_image.ProperName()} — Gradiwin";
        Title = TitleText.Text;
    }

    // ===================================================================================
    // Canvas wiring
    // ===================================================================================

    private void WireCanvas()
    {
        Canvas.ToolOptionsProvider = mode => _tools.GetOptions(mode);
        Canvas.SetMode(_tools.CurrentMode);
        Canvas.Document.Changed += UpdateHistoryButtons;
        Canvas.SelectionChanged += a =>
        {
            EraseSelectedPanel.Visibility = a is null ? Visibility.Collapsed : Visibility.Visible;
            if (a is null) _tools.ExitTempEditing();
            else _tools.EnterTempEditing(a);
        };
        Canvas.ZoomChanged += z => ZoomLabel.Content = $"{Math.Round(z * 100)}%";
        Canvas.ToolSizeAdjustRequested += delta => _tools.AdjustSize(delta);
        Canvas.TextEntryRequested += ShowTextEntry;
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = Canvas.Document.CanUndo;
        RedoButton.IsEnabled = Canvas.Document.CanRedo;
        ClearButton.IsEnabled = !Canvas.Document.IsEmpty;
        if (Canvas.Selected is { } s && !Canvas.Document.Items.Contains(s))
            Canvas.ClearSelection();
        Canvas.Invalidate();
    }

    /// <summary>Port of overlay/text_entry_popover.py.</summary>
    private void ShowTextEntry(PointD imagePos, Windows.Foundation.Point viewPos, TextAnnotation? editing)
    {
        double fontSize = editing?.FontSize ?? Canvas.TextFontSize;
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 240,
            MaxHeight = 160,
            PlaceholderText = Localizer.T("Enter text"),
            Text = editing?.Text ?? "",
        };
        var size = new NumberBox
        {
            Minimum = 8,
            Maximum = 72,
            SmallChange = 4,
            LargeChange = 4,
            Value = fontSize,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTipService.SetToolTip(size, Localizer.T("Font Size"));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, size } };
        var flyout = new Flyout { Content = panel, ShouldConstrainToRootBounds = false };
        bool cancelled = false;

        Canvas.BeginTextPreview(imagePos, editing, fontSize);
        box.TextChanged += (_, _) => Canvas.UpdateTextPreview(box.Text, size.Value);
        size.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            if (editing is null) Canvas.TextFontSize = e.NewValue;
            Canvas.UpdateTextPreview(box.Text, e.NewValue);
        };
        box.KeyDown += (_, e) =>
        {
            bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (e.Key == VirtualKey.Enter && !shift)
            {
                e.Handled = true;
                flyout.Hide();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                cancelled = editing is null && box.Text.Length == 0;
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        flyout.Closed += (_, _) =>
        {
            Canvas.EndTextPreview(commit: !cancelled);
            Canvas.Focus(FocusState.Programmatic);
        };
        flyout.ShowAt(Canvas, new FlyoutShowOptions { Position = viewPos, Placement = FlyoutPlacementMode.Bottom });
    }

    // ===================================================================================
    // Loading
    // ===================================================================================

    public async Task LoadFileAsync(string path, ImageOrigin origin, string? screenshotPath = null, bool copyAfterProcessing = false)
    {
        ShowEditor();
        Loading.IsActive = true;
        try
        {
            var image = await LoadedImage.LoadAsync(path, origin, screenshotPath);
            image.CopyAfterProcessing = copyAfterProcessing;
            SetImage(image);
            if (origin is ImageOrigin.Screenshot)
                DeleteScreenshotsItem.IsEnabled = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException || e.HResult != 0)
        {
            ShowToast(Localizer.T("Failed to load image") + ": " + e.Message);
            if (_image is null)
                ShowWelcome();
        }
        finally
        {
            Loading.IsActive = false;
        }
    }

    /// <summary>Port of GradiaMainWindow.set_image.</summary>
    private void SetImage(LoadedImage image)
    {
        _image?.Dispose();
        _image = image;
        Canvas.Document.Reset();
        Canvas.ClearSelection();
        ResetCropSelection();
        Canvas.ResetZoom();
        _imageOptions.ResetRotation();
        _showCloseConfirmation = true;
        UpdateFileInfo();
        Reprocess();

        _imageReady = true;
        SaveButton.IsEnabled = CopyButton.IsEnabled = ShareButton.IsEnabled = true;
        if (image.CopyAfterProcessing)
            _ = CopyToClipboardAsync(silent: true);
    }

    private void ShowEditor()
    {
        Welcome.Visibility = Visibility.Collapsed;
        Split.Visibility = Visibility.Visible;
        SidebarToggle.Visibility = Visibility.Visible;
        SidebarToggle.IsChecked = Split.IsPaneOpen;
        UpdateCompact();
    }

    private void ShowWelcome()
    {
        Welcome.Visibility = Visibility.Visible;
        Split.Visibility = Visibility.Collapsed;
        SidebarToggle.Visibility = Visibility.Collapsed;
    }

    // ===================================================================================
    // Processing
    // ===================================================================================

    private ImageOptions CurrentOptions => _imageOptions.GetOptions(_background.Mode != BackgroundMode.None);

    private void ScheduleReprocess()
    {
        if (_reprocessQueued || _image is null) return;
        _reprocessQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _reprocessQueued = false;
            Reprocess();
        });
    }

    private void Reprocess()
    {
        if (_image is null) return;
        try
        {
            var processed = ImageProcessor.Process(_image, CurrentOptions, _background.CurrentSpec, fullResolution: false);
            Canvas.SetProcessed(processed);
            _processed?.Dispose();
            _processed = processed;

            var full = ImageProcessor.ComputeLayout(_image, CurrentOptions, fullResolution: true);
            _imageSize.Text = $"{full.FinalWidth}×{full.FinalHeight}";
            if (_cropAspect > 0)
                Canvas.Crop.ApplyAspectRatio(Canvas.ImageRect.Width, Canvas.ImageRect.Height);
        }
        catch (Exception e) when (e.HResult != 0)
        {
            ShowToast(Localizer.T("Error processing image") + ": " + e.Message);
        }
    }

    /// <summary>The final image (full resolution, annotations, crop).</summary>
    private CanvasRenderTarget? RenderFinal()
    {
        if (_image is null || _processed is null) return null;
        return ExportRenderer.Render(_image, CurrentOptions, _background.CurrentSpec, Canvas.Document.Items,
            Canvas.Crop.Rect, (int)_processed.SizeInPixels.Width, (int)_processed.SizeInPixels.Height);
    }

    // ===================================================================================
    // Export (image_exporters.py)
    // ===================================================================================

    private string SuggestedFileName()
    {
        if (_image is null) return "Image";
        var name = _image.ProperName(withExtension: false);
        return _image.HasProperName ? $"{name} ({Localizer.T("Edit")})" : name;
    }

    private async Task SaveToFileAsync()
    {
        if (!_imageReady) return;
        var picker = WindowHelper.CreateSavePicker(this);
        var preferred = ImageEncoder.Get(_settings.ExportFormat);
        foreach (var f in ImageEncoder.Formats.OrderBy(f => f.Key == preferred.Key ? 0 : 1))
            picker.FileTypeChoices.Add(Localizer.T(f.Name), f.Extensions.ToList());
        picker.SuggestedFileName = SuggestedFileName();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        var format = ImageEncoder.FromExtension(file.Path);
        if (format is null)
        {
            ShowToast(Localizer.T("Unsupported image file extension"));
            return;
        }
        try
        {
            using var final = RenderFinal();
            if (final is null) return;
            await ImageEncoder.SaveAsync(final, file.Path, format, _settings.ExportCompress);
            _showCloseConfirmation = false;
            ShowToast(Localizer.T("Image Saved"), Localizer.T("Open Folder"), () => WindowHelper.OpenFolder(null, file.Path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException || e.HResult != 0)
        {
            ShowToast(Localizer.T("Export Failed") + ": " + e.Message);
        }
    }

    private async Task<bool> CopyToClipboardAsync(bool silent = false)
    {
        try
        {
            using var final = RenderFinal();
            if (final is null) return false;
            await ClipboardService.CopyImageAsync(final);
            if (!silent)
            {
                _showCloseConfirmation = false;
                ShowToast(Localizer.T("Image Copied"));
            }
            return true;
        }
        catch (Exception e) when (e.HResult != 0)
        {
            ShowToast(Localizer.T("Failed to copy image to clipboard"));
            System.Diagnostics.Debug.WriteLine(e);
            return false;
        }
    }

    private async Task<string?> RenderToTempPngAsync(string name)
    {
        using var final = RenderFinal();
        if (final is null) return null;
        var path = _session.NewPath(name);
        await ImageEncoder.SaveAsync(final, path, ImageEncoder.Get("png"), false);
        return path;
    }

    private async Task ShareSheetAsync()
    {
        try
        {
            var path = await RenderToTempPngAsync(SuggestedFileName() + ".png");
            if (path != null)
                await ShareHelper.ShareFileAsync(WindowHelper.GetHwnd(this), path, SuggestedFileName());
        }
        catch (Exception e) when (e.HResult != 0)
        {
            ShowToast(Localizer.T("Sharing failed") + ": " + e.Message);
        }
    }

    /// <summary>Port of CommandLineExporter.run_custom_command (Ctrl+M).</summary>
    private async Task UploadAsync()
    {
        if (!_imageReady) return;
        if (!UploadService.IsConfigured)
        {
            ShowToast(Localizer.T("No upload provider selected"), Localizer.T("Choose"), () => OpenPreferences(PreferencesWindow.Page.Upload));
            return;
        }
        if (_settings.ShowExportConfirmDialog)
        {
            var dontAsk = new CheckBox { Content = Localizer.T("Don't Ask Again") };
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = Localizer.T("Upload Image?"),
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = Localizer.F("The image will be uploaded to {0}. Anyone with the link can view it.", UploadService.ProviderDisplayName ?? ""), TextWrapping = TextWrapping.Wrap },
                        dontAsk,
                    },
                },
                PrimaryButtonText = Localizer.T("Upload"),
                CloseButtonText = Localizer.T("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (dontAsk.IsChecked == true) _settings.ShowExportConfirmDialog = false;
        }

        ShowToast(Localizer.T("Uploading…"));
        try
        {
            var path = await RenderToTempPngAsync("clipboard_temp.png");
            if (path is null) return;
            var result = await UploadService.UploadAsync(path);
            if (!result.Success)
            {
                ShowToast(result.Output);
                return;
            }
            if (result.Warning != null)
                ShowToast(Localizer.T("Warning: ") + result.Warning);
            if (result.Output.Length == 0)
            {
                ShowToast(Localizer.T("No output from command"));
                return;
            }
            ClipboardService.CopyText(result.Output);
            _showCloseConfirmation = false;
            if (result.IsUrl)
                ShowToast(Localizer.T("Link generated successfully"), Localizer.T("Open Link"), () => _ = WindowHelper.OpenUriAsync(result.Output));
            else
                ShowToast(Localizer.T("Result copied to clipboard"));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException || e.HResult != 0)
        {
            ShowToast(Localizer.T("Failed to run custom export command") + ": " + e.Message);
        }
    }

    // ===================================================================================
    // Import (image_loaders.py)
    // ===================================================================================

    private async Task OpenFileDialogAsync()
    {
        var picker = WindowHelper.CreateOpenPicker(this);
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp", ".avif", ".bmp" })
            picker.FileTypeFilter.Add(ext);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        var file = await picker.PickSingleFileAsync();
        if (file != null)
            await LoadFileAsync(file.Path, ImageOrigin.FileDialog);
    }

    private async Task PasteAsync()
    {
        try
        {
            var result = await ClipboardService.ReadImageAsync(_session.Folder);
            if (result is { } r)
                await LoadFileAsync(r.Path, r.FromFile ? ImageOrigin.FileDialog : ImageOrigin.Clipboard);
            else
                ShowToast(Localizer.T("No image found in clipboard"));
        }
        catch (Exception e) when (e.HResult != 0)
        {
            ShowToast(Localizer.T("Failed to paste image") + ": " + e.Message);
        }
    }

    private Task TakeScreenshotAsync(ScreenshotMode mode) =>
        App.Current.TakeScreenshotIntoNewWindowAsync(mode, 0, this);

    private void OpenSourceSnippet()
    {
        var w = new SourceSnippetWindow(async path =>
        {
            await LoadFileAsync(path, ImageOrigin.SourceImage);
            WindowHelper.BringToFront(this);
        });
        App.Current.Track(w);
        w.Activate();
    }

    // Drag and drop (overlay/drop_overlay.py)
    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.WebLink) ||
            e.DataView.Contains(StandardDataFormats.Uri) || e.DataView.Contains(StandardDataFormats.Bitmap))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Localizer.T("Open Image");
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void Root_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        var def = e.GetDeferral();
        try
        {
            var data = e.DataView;
            if (data.Contains(StandardDataFormats.StorageItems))
            {
                var files = (await data.GetStorageItemsAsync()).OfType<StorageFile>().Where(f => ClipboardService.IsImageFile(f.Path)).ToList();
                if (files.Count > 0)
                {
                    await LoadFileAsync(files[0].Path, ImageOrigin.DragDrop);
                    foreach (var extra in files.Skip(1))
                        await App.Current.OpenWindow().LoadFileAsync(extra.Path, ImageOrigin.DragDrop);
                    return;
                }
            }
            Uri? uri = null;
            if (data.Contains(StandardDataFormats.WebLink)) uri = await data.GetWebLinkAsync();
            else if (data.Contains(StandardDataFormats.Uri)) uri = await data.GetUriAsync();
            if (uri is { Scheme: "http" or "https" })
            {
                ShowEditor();
                Loading.IsActive = true;
                var path = await Downloader.DownloadImageAsync(uri, _session.Folder);
                await LoadFileAsync(path, ImageOrigin.DragDrop);
                return;
            }
            if (data.Contains(StandardDataFormats.Bitmap))
            {
                var bmp = await data.GetBitmapAsync();
                using var stream = await bmp.OpenReadAsync();
                var path = _session.NewUniquePath("dropped", ".png");
                await using (var fs = File.Create(path))
                    await stream.AsStreamForRead().CopyToAsync(fs);
                await LoadFileAsync(path, ImageOrigin.Clipboard);
            }
        }
        catch (Exception ex) when (ex.HResult != 0)
        {
            Loading.IsActive = false;
            ShowToast(Localizer.T("Failed to load dropped item") + ": " + ex.Message);
        }
        finally
        {
            def.Complete();
        }
    }

    // ===================================================================================
    // Crop (image_stack.py)
    // ===================================================================================

    private void ToggleCrop()
    {
        if (_image is null) return;
        if (Canvas.CropInteractive)
        {
            ConfirmCrop();
            return;
        }
        _cropBackup = Canvas.Crop.Rect;
        if (!Canvas.Crop.HasCrop)
            Canvas.Crop.Set(CropRect.Initial);
        if (_cropAspect > 0)
            Canvas.Crop.SetAspectRatio(_cropAspect, Canvas.ImageRect.Width, Canvas.ImageRect.Height);
        Canvas.SetCropInteractive(true);
        UpdateCropUi();
    }

    private void ConfirmCrop()
    {
        Canvas.SetCropInteractive(false);
        UpdateCropUi();
    }

    private void CropBack()
    {
        if (!Canvas.CropInteractive) return;
        Canvas.Crop.Set(_cropBackup);
        Canvas.SetCropInteractive(false);
        UpdateCropUi();
    }

    private void ResetCropSelection()
    {
        Canvas.Crop.Set(CropRect.Full);
        Canvas.SetCropInteractive(false);
        UpdateCropUi();
    }

    private void UpdateCropUi()
    {
        bool interactive = Canvas.CropInteractive;
        CropToggle.IsChecked = interactive;
        CropConfirmPanel.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
        CropAspectButton.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
        ResetCropButton.Visibility = interactive || Canvas.Crop.HasCrop ? Visibility.Visible : Visibility.Collapsed;
        Canvas.Invalidate();
    }

    private void BuildCropAspectMenu()
    {
        var menu = new MenuFlyout();
        foreach (var (label, ratio, icon) in Palettes.CropAspectRatios)
        {
            var item = new MenuFlyoutItem { Text = Localizer.T(label) };
            var r = ratio ?? 0;
            var ic = icon;
            item.Click += (_, _) =>
            {
                _cropAspect = r;
                CropAspectIcon.Icon = ic;
                Canvas.Crop.SetAspectRatio(r, Canvas.ImageRect.Width, Canvas.ImageRect.Height);
                Canvas.Invalidate();
            };
            menu.Items.Add(item);
        }
        CropAspectButton.Flyout = menu;
    }

    // ===================================================================================
    // OCR
    // ===================================================================================

    private async Task RunOcrAsync()
    {
        if (_processed is null) return;
        using var region = ExportRenderer.RenderPreview(_processed, Canvas.Document.Items, Canvas.Crop.Rect, includeAnnotations: false);
        // Run OCR on the full-resolution source when there is no crop for better accuracy.
        CanvasBitmap source = region;
        CanvasRenderTarget? full = null;
        if (!Canvas.Crop.HasCrop && _image != null)
        {
            full = ExportRenderer.Render(_image, CurrentOptions, _background.CurrentSpec, [], CropRect.Full,
                (int)_processed.SizeInPixels.Width, (int)_processed.SizeInPixels.Height);
            source = full;
        }
        try
        {
            await OcrDialog.ShowAsync(Root.XamlRoot, source);
        }
        finally
        {
            full?.Dispose();
        }
    }

    // ===================================================================================
    // Toasts (Adw.Toast equivalent)
    // ===================================================================================

    public void ShowToast(string message, string? actionLabel = null, Action? action = null)
    {
        var bar = new InfoBar
        {
            Message = message,
            IsOpen = true,
            IsClosable = true,
            Severity = InfoBarSeverity.Informational,
            IsIconVisible = false,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorTertiaryBrush"],
        };
        if (actionLabel != null && action != null)
        {
            var b = new Button { Content = actionLabel };
            b.Click += (_, _) =>
            {
                action();
                bar.IsOpen = false;
            };
            bar.ActionButton = b;
        }
        bar.Closed += (_, _) => ToastHost.Children.Remove(bar);
        while (ToastHost.Children.Count >= 3)
            ToastHost.Children.RemoveAt(0);
        ToastHost.Children.Add(bar);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(actionLabel != null ? 6 : 3);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => bar.IsOpen = false;
        timer.Start();
    }

    // ===================================================================================
    // Keyboard shortcuts (window._setup_actions)
    // ===================================================================================

    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        return focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox ||
               (focused is FrameworkElement fe && fe.Parent is NumberBox) ||
               (focused is ComboBox { IsEditable: true });
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool textFocused = IsTextInputFocused();
        if (alt) return;
        var key = e.Key;

        if (ctrl)
        {
            switch (key)
            {
                case VirtualKey.O when shift: Do(() => _ = RunOcrAsync()); break;
                case VirtualKey.O: Do(() => _ = OpenFileDialogAsync()); break;
                case VirtualKey.A when !textFocused: Do(() => _ = TakeScreenshotAsync(ScreenshotMode.Interactive)); break;
                case VirtualKey.V when !textFocused: Do(() => _ = PasteAsync()); break;
                case VirtualKey.P: Do(OpenSourceSnippet); break;
                case VirtualKey.S when _imageReady: Do(() => _ = SaveToFileAsync()); break;
                case VirtualKey.C when !textFocused && _imageReady: Do(() => _ = CopyToClipboardAsync()); break;
                case VirtualKey.M when _imageReady: Do(() => _ = UploadAsync()); break;
                case VirtualKey.X when !textFocused: Do(() => Canvas.DeleteSelected()); break;
                case VirtualKey.Z when !textFocused && shift: Do(() => Canvas.Document.Redo()); break;
                case VirtualKey.Z when !textFocused: Do(() => Canvas.Document.Undo()); break;
                case VirtualKey.Y when !textFocused: Do(() => Canvas.Document.Redo()); break;
                case VirtualKey.R when shift: Do(ResetCropSelection); break;
                case VirtualKey.R: Do(ToggleCrop); break;
                case VirtualKey.D when shift: Do(() => _ = DeleteScreenshotsAsync()); break;
                case (VirtualKey)187 or VirtualKey.Add: Do(() => Canvas.ZoomIn()); break; // '=' / '+'
                case (VirtualKey)189 or VirtualKey.Subtract: Do(() => Canvas.ZoomOut()); break;
                case VirtualKey.Number0 or VirtualKey.NumberPad0: Do(() => Canvas.ResetZoom()); break;
                case VirtualKey.Left when !textFocused: Do(() => Canvas.PanBy(50, 0)); break;
                case VirtualKey.Right when !textFocused: Do(() => Canvas.PanBy(-50, 0)); break;
                case VirtualKey.Up when !textFocused: Do(() => Canvas.PanBy(0, 50)); break;
                case VirtualKey.Down when !textFocused: Do(() => Canvas.PanBy(0, -50)); break;
                case (VirtualKey)188: Do(() => OpenPreferences()); break; // ','
                case (VirtualKey)191: Do(() => _ = ShortcutsDialog.ShowAsync(Root.XamlRoot)); break; // '/' or '?'
                case VirtualKey.Q or VirtualKey.W: Do(Close); break;
            }
            return;
        }

        if (key == VirtualKey.F9 && HasImage)
        {
            Do(ToggleSidebar);
            return;
        }
        if (key == VirtualKey.Escape && Canvas.CropInteractive)
        {
            Do(CropBack);
            return;
        }
        if (textFocused || !HasImage || Canvas.LiveText is not null)
            return;
        if (key == VirtualKey.Delete)
        {
            Do(() => Canvas.DeleteSelected());
            return;
        }

        // Bare-key tool shortcuts, disabled while typing (disable_on_entry_focus).
        string? ch = key switch
        {
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)(key - VirtualKey.Number0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => ((int)(key - VirtualKey.NumberPad0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            (VirtualKey)192 => "`",
            >= VirtualKey.A and <= VirtualKey.Z => ((char)('a' + (key - VirtualKey.A))).ToString(),
            _ => null,
        };
        if (ch is null || shift) return;
        var tool = ToolDefinition.All.FirstOrDefault(t => t.Shortcuts.Contains(ch));
        if (tool != null)
        {
            Do(() =>
            {
                _tools.SetCurrentTool(tool.Mode);
                Canvas.SetMode(tool.Mode);
            });
        }

        void Do(Action a)
        {
            e.Handled = true;
            a();
        }
    }

    // ===================================================================================
    // Layout
    // ===================================================================================

    private void UpdateCaptionInset()
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1;
        CaptionColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);
    }

    private void UpdateCompact()
    {
        bool compact = Root.ActualWidth < CompactBreakpoint;
        if (compact == _isCompact) return;
        _isCompact = compact;
        Split.DisplayMode = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        Split.IsPaneOpen = !compact && _settings.SidebarVisible;
        SidebarToggle.IsChecked = Split.IsPaneOpen;
    }

    private void ToggleSidebar()
    {
        Split.IsPaneOpen = !Split.IsPaneOpen;
        if (!_isCompact) _settings.SidebarVisible = Split.IsPaneOpen;
        SidebarToggle.IsChecked = Split.IsPaneOpen;
    }

    private void Split_PaneClosed(SplitView sender, object args) => SidebarToggle.IsChecked = false;
    private void Split_PaneOpened(SplitView sender, object args) => SidebarToggle.IsChecked = true;

    // ===================================================================================
    // Close handling (window._on_close_request / CloseHandlerExporter)
    // ===================================================================================

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed) return;
        args.Cancel = true;

        var method = _settings.ExitMethod;
        bool copy;
        if (method == "confirm" && _showCloseConfirmation && _imageReady)
        {
            var choice = await ConfirmCloseDialog.ShowAsync(Root.XamlRoot);
            if (choice is null) return;
            copy = choice.Value;
        }
        else
        {
            copy = method == "copy" && _imageReady;
        }
        await FinalizeCloseAsync(copy);
    }

    private async Task FinalizeCloseAsync(bool copy)
    {
        bool trash = _settings.TrashScreenshotsOnClose;
        bool save = !trash && _settings.OverwriteScreenshot;
        AppWindow.Hide();
        try
        {
            if (_imageReady && _image is not null)
            {
                bool saved = false;
                if (save && _image.IsScreenshot && _image.ScreenshotPath is { } shotPath && ImageEncoder.FromExtension(shotPath) is { } fmt)
                {
                    using var final = RenderFinal();
                    if (final != null)
                    {
                        await ImageEncoder.SaveAsync(final, shotPath, fmt, _settings.ExportCompress);
                        saved = true;
                    }
                }
                if (copy && await CopyToClipboardAsync(silent: true))
                {
                    NotificationService.Show(Localizer.T("Image Copied"), Localizer.T("You can now paste it from the clipboard."),
                        openFolder: saved ? _image.FolderPath : null);
                }
            }
            if (trash)
                RecycleBin.Recycle(ScreenshotService.TakenScreenshots);
        }
        catch (Exception e) when (e.HResult != 0)
        {
            Log.Error("Close export failed", e);
            NotificationService.Show(Localizer.T("Close Operation Failed"), Localizer.T("Failed to export image") + ": " + e.Message);
        }
        _closeConfirmed = true;
        Close();
    }

    private async Task DeleteScreenshotsAsync()
    {
        var shots = ScreenshotService.TakenScreenshots.Where(File.Exists).ToList();
        if (shots.Count == 0) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Localizer.T("Delete Screenshots?"),
            Content = Localizer.F("{0} screenshot(s) taken in this session will be moved to the Recycle Bin.", shots.Count),
            PrimaryButtonText = Localizer.T("Delete"),
            CloseButtonText = Localizer.T("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        int n = RecycleBin.Recycle(shots);
        ScreenshotService.TakenScreenshots.Clear();
        DeleteScreenshotsItem.IsEnabled = false;
        ShowToast(Localizer.F("{0} screenshot(s) moved to the Recycle Bin", n));
    }

    // ===================================================================================
    // Preferences
    // ===================================================================================

    public void OpenPreferences(PreferencesWindow.Page page = PreferencesWindow.Page.General)
    {
        var w = PreferencesWindow.Show(page);
        w.Closed -= Preferences_Closed;
        w.Closed += Preferences_Closed;
    }

    private void Preferences_Closed(object sender, WindowEventArgs args)
    {
        UploadService_Updated();
        Welcome.RefreshRecent();
    }

    private void UploadService_Updated()
    {
        var name = UploadService.ProviderDisplayName;
        UploadItem.Text = name is null ? Localizer.T("Upload") : Localizer.F("Upload to {0}", name);
        UploadItem.IsEnabled = UploadService.IsConfigured;
    }

    // ===================================================================================
    // XAML event handlers
    // ===================================================================================

    private void SidebarToggle_Click(object sender, RoutedEventArgs e) => ToggleSidebar();
    private void Open_Click(object sender, RoutedEventArgs e) => _ = OpenFileDialogAsync();
    private void Screenshot_Click(object sender, RoutedEventArgs e) => _ = TakeScreenshotAsync(ScreenshotMode.Interactive);
    private void SourceSnippet_Click(object sender, RoutedEventArgs e) => OpenSourceSnippet();
    private void DeleteScreenshots_Click(object sender, RoutedEventArgs e) => _ = DeleteScreenshotsAsync();
    private void Preferences_Click(object sender, RoutedEventArgs e) => OpenPreferences();
    private void Shortcuts_Click(object sender, RoutedEventArgs e) => _ = ShortcutsDialog.ShowAsync(Root.XamlRoot);
    private void Donate_Click(object sender, RoutedEventArgs e) => _ = WindowHelper.OpenUriAsync("https://ko-fi.com/alexandervanhee");
    private void About_Click(object sender, RoutedEventArgs e) => _ = AboutDialog.ShowAsync(Root.XamlRoot);
    private void Save_Click(object sender, RoutedEventArgs e) => _ = SaveToFileAsync();
    private void Copy_Click(object sender, RoutedEventArgs e) => _ = CopyToClipboardAsync();
    private void Share_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (UploadService.IsConfigured) _ = UploadAsync();
        else _ = ShareSheetAsync();
    }
    private void ShareSheet_Click(object sender, RoutedEventArgs e) => _ = ShareSheetAsync();
    private void Upload_Click(object sender, RoutedEventArgs e) => _ = UploadAsync();
    private void ChooseProvider_Click(object sender, RoutedEventArgs e) => OpenPreferences(PreferencesWindow.Page.Upload);
    private void CropToggle_Click(object sender, RoutedEventArgs e) => ToggleCrop();
    private void ResetCrop_Click(object sender, RoutedEventArgs e) => ResetCropSelection();
    private void CropBack_Click(object sender, RoutedEventArgs e) => CropBack();
    private void CropConfirm_Click(object sender, RoutedEventArgs e) => ConfirmCrop();
    private void Ocr_Click(object sender, RoutedEventArgs e) => _ = RunOcrAsync();
    private void EraseSelected_Click(object sender, RoutedEventArgs e) => Canvas.DeleteSelected();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Canvas.ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Canvas.ZoomOut();
    private void ResetZoom_Click(object sender, RoutedEventArgs e) => Canvas.ResetZoom();
    private void Undo_Click(object sender, RoutedEventArgs e) => Canvas.Document.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Canvas.Document.Redo();
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Canvas.Document.Clear();
        Canvas.ClearSelection();
    }
}
