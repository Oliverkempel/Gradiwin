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
        var flyout = new Flyout
        {
            Content = new ScrollViewer { Content = panel, MaxHeight = 640 },
            Placement = FlyoutPlacementMode.Auto,
        };
        ok.Click += (_, _) =>
        {
            onPicked(ColorUtil.ToRgba(picker.Color));
            flyout.Hide();
        };
        // The picker is tall; with Placement=Auto WinUI throws ArgumentException ("Value does not fall within
        // the expected range") when it can't fit on any side of the anchor, which always happened inside the
        // gradient steps dialog. Fall back to opening below the anchor, allowed to extend past the window.
        flyout.XamlRoot = anchor.XamlRoot;
        try
        {
            flyout.ShowAt(anchor);
        }
        catch (ArgumentException)
        {
            flyout.Placement = FlyoutPlacementMode.Bottom;
            flyout.ShouldConstrainToRootBounds = false;
            flyout.ShowAt(anchor);
        }
    }
}
