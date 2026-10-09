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
            IsMoreButtonVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
        };
        var ok = new Button { Content = Localizer.T("Select"), Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 8, Children = { picker, ok } };
        // Placement=Auto made FlyoutBase.ShowAt throw ArgumentException ("Value does not fall within the
        // expected range") for this picker in practice, from the sidebar and from dialogs alike, while an
        // explicit placement always works. Open below the anchor and let the popup extend beyond the window
        // (Windows keeps it on screen); the height cap fits a typical laptop screen, and it scrolls if needed.
        var flyout = new Flyout
        {
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            Placement = FlyoutPlacementMode.Bottom,
            ShouldConstrainToRootBounds = false,
        };
        ok.Click += (_, _) =>
        {
            onPicked(ColorUtil.ToRgba(picker.Color));
            flyout.Hide();
        };
        flyout.XamlRoot = anchor.XamlRoot; // required when the anchor is inside a ContentDialog
        flyout.ShowAt(anchor);
    }
}
