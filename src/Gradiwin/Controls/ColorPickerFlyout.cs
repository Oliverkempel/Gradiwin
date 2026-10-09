// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Models;
using Gradiwin.Rendering;
using Gradiwin.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Gradiwin.Controls;

/// <summary>The "More Colors" dialog (Gtk.ColorDialog equivalent) as a flyout with a full ColorPicker.</summary>
public static class ColorPickerFlyout
{
    public static void Show(FrameworkElement anchor, RgbaColor initial, bool withAlpha, Action<RgbaColor> onPicked)
    {
        var picker = new ColorPicker
        {
            Color = ColorUtil.ToColor(initial.A <= 0 ? initial.WithAlpha(1) : initial),
            IsAlphaEnabled = withAlpha,
            IsAlphaTextInputVisible = withAlpha,
            // No "More" toggle (it pushed the RGB fields and a lone button off to the right):
            // the hex field is always visible, which covers typing an exact color.
            IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // Balanced footer: Cancel | Select, each half the width (like a ContentDialog).
        var cancel = new Button { Content = Localizer.T("Cancel"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var ok = new Button
        {
            Content = Localizer.T("Select"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var footer = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.Children.Add(cancel);
        Grid.SetColumn(ok, 1);
        footer.Children.Add(ok);

        var panel = new StackPanel { Spacing = 12, Children = { picker, footer } };

        // Subtle translucency: transient surfaces use acrylic in Fluent (Mica is for long-lived windows);
        // the thin variant is the lighter one Windows 11 uses for its own flyouts.
        // The presenter's own fill is made transparent so the backdrop shows through.
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.BackgroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16)));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 400.0));

        // Placement=Auto made FlyoutBase.ShowAt throw ArgumentException ("Value does not fall within the
        // expected range") for this picker in practice, from the sidebar and from dialogs alike, while an
        // explicit placement always works. Open below the anchor and let the popup extend beyond the window
        // (Windows keeps it on screen); the height cap fits a typical laptop screen, and it scrolls if needed.
        var flyout = new Flyout
        {
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            Placement = FlyoutPlacementMode.Bottom,
            ShouldConstrainToRootBounds = false,
            FlyoutPresenterStyle = presenterStyle,
            SystemBackdrop = new ThinAcrylicBackdrop(),
        };
        cancel.Click += (_, _) => flyout.Hide();
        ok.Click += (_, _) =>
        {
            onPicked(ColorUtil.ToRgba(picker.Color));
            flyout.Hide();
        };
        flyout.XamlRoot = anchor.XamlRoot; // required when the anchor is inside a ContentDialog
        flyout.ShowAt(anchor);
    }
}
