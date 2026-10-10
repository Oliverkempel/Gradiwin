// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Gradiwin.Core.Models;
using Gradiwin.Rendering;
using Gradiwin.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.UI;

namespace Gradiwin.Controls;

/// <summary>
/// The "More Colors" dialog (Gtk.ColorDialog equivalent): a color wheel with its sliders, a collapsible
/// "More" section with exact inputs (hex, opacity, red/green/blue), and a Cancel | Select footer.
/// </summary>
public static class ColorPickerFlyout
{
    public static void Show(FrameworkElement anchor, RgbaColor initial, bool withAlpha, Action<RgbaColor> onPicked)
    {
        // The ColorPicker's own text inputs are hidden: they come in mismatched widths with only one label.
        // The "More" section below provides consistent, labelled replacements.
        var picker = new ColorPicker
        {
            Color = ColorUtil.ToColor(initial.A <= 0 ? initial.WithAlpha(1) : initial),
            IsAlphaEnabled = withAlpha,
            IsAlphaSliderVisible = withAlpha,
            IsAlphaTextInputVisible = false,
            IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = false,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var more = new Expander
        {
            Header = Localizer.T("More"),
            Content = BuildExactInputs(picker, withAlpha),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = false,
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

        var panel = new StackPanel { Spacing = 12, Children = { picker, more, footer } };

        // Subtle translucency: transient surfaces use acrylic in Fluent (Mica is for long-lived windows).
        // The presenter's own fill is made transparent so the backdrop shows through.
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.BackgroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16)));
        // Windows 11 flyout corners (OverlayCornerRadius, 8 px).
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, Application.Current.Resources["OverlayCornerRadius"]));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 400.0));

        // Placement=Auto made FlyoutBase.ShowAt throw ArgumentException ("Value does not fall within the
        // expected range") for this picker in practice, from the sidebar and from dialogs alike, while an
        // explicit placement always works. Open below the anchor and let the popup extend beyond the window
        // (Windows keeps it on screen); the height cap fits a typical laptop screen, and it scrolls if needed.
        var flyout = new Flyout
        {
            Content = new ScrollViewer { Content = panel, MaxHeight = 600 },
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

    /// <summary>
    /// Hex and opacity on one row, red/green/blue on the next; every field has a label above it and the
    /// fields in a row share the width equally. Kept in sync with the color wheel in both directions.
    /// </summary>
    private static FrameworkElement BuildExactInputs(ColorPicker picker, bool withAlpha)
    {
        bool updating = false;

        var hex = new TextBox { Header = Localizer.T("Hex"), MaxLength = 7, IsSpellCheckEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        NumberBox Number(string header, double max) => new()
        {
            Header = Localizer.T(header),
            Minimum = 0,
            Maximum = max,
            SmallChange = 1,
            LargeChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var opacity = Number("Opacity (%)", 100);
        var red = Number("Red", 255);
        var green = Number("Green", 255);
        var blue = Number("Blue", 255);

        void FromPicker()
        {
            updating = true;
            var c = picker.Color;
            hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            opacity.Value = Math.Round(c.A / 255.0 * 100);
            red.Value = c.R;
            green.Value = c.G;
            blue.Value = c.B;
            updating = false;
        }

        // Fields may only change the color while the user is editing that field. NumberBox raises
        // ValueChanged asynchronously, so values written by FromPicker() during a wheel drag would otherwise
        // come back as "edits" and fight the drag (the knob jumped around). Also skip no-op updates: setting
        // the same color re-derives hue/saturation and can move the knob.
        void ToPicker(Color c, Control? source)
        {
            if (updating || source?.FocusState == FocusState.Unfocused || c == picker.Color) return;
            updating = true;
            picker.Color = c;
            updating = false;
        }

        static byte Channel(double v) => (byte)Math.Clamp(double.IsNaN(v) ? 0 : Math.Round(v), 0, 255);

        void ApplyHex()
        {
            var text = hex.Text.Trim().TrimStart('#');
            if (text.Length == 3)
                text = string.Concat(text.Select(ch => new string(ch, 2)));
            if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            {
                ToPicker(Color.FromArgb(picker.Color.A, (byte)(v >> 16), (byte)(v >> 8), (byte)v), null); // Enter/leaving the hex box is always an explicit edit
                FromPicker(); // normalise casing / short form
            }
            else
            {
                FromPicker(); // invalid input: show the current color again
            }
        }

        picker.ColorChanged += (_, _) => { if (!updating) FromPicker(); };
        // Apply on Enter or when leaving the box.
        hex.LostFocus += (_, _) => ApplyHex();
        hex.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) ApplyHex(); };
        opacity.ValueChanged += (_, e) =>
        {
            var c = picker.Color;
            double percent = double.IsNaN(e.NewValue) ? 100 : e.NewValue;
            ToPicker(Color.FromArgb((byte)Math.Clamp(Math.Round(percent / 100 * 255), 0, 255), c.R, c.G, c.B), opacity);
        };
        red.ValueChanged += (_, e) => { var c = picker.Color; ToPicker(Color.FromArgb(c.A, Channel(e.NewValue), c.G, c.B), red); };
        green.ValueChanged += (_, e) => { var c = picker.Color; ToPicker(Color.FromArgb(c.A, c.R, Channel(e.NewValue), c.B), green); };
        blue.ValueChanged += (_, e) => { var c = picker.Color; ToPicker(Color.FromArgb(c.A, c.R, c.G, Channel(e.NewValue)), blue); };
        FromPicker();

        Grid Row(params FrameworkElement[] fields)
        {
            var g = new Grid { ColumnSpacing = 8 };
            for (int i = 0; i < fields.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(fields[i], i);
                g.Children.Add(fields[i]);
            }
            return g;
        }

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                withAlpha ? Row(hex, opacity) : Row(hex),
                Row(red, green, blue),
            },
        };
    }
}
