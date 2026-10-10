// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Gradiwin.Controls;

/// <summary>
/// "Thin" desktop acrylic, the lighter, more translucent material Windows 11 uses for its own menus and
/// flyouts. The built-in <see cref="DesktopAcrylicBackdrop"/> uses the default kind, which in dark theme is
/// tinted so heavily that it reads as a solid fill.
/// </summary>
public sealed partial class ThinAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!DesktopAcrylicController.IsSupported())
            return; // e.g. transparency effects turned off: the flyout keeps its regular fill
        var config = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        bool light = config.Theme == SystemBackdropTheme.Light;
        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin,
            // Thin acrylic on its own lets too much through; a heavier tint in the flyout color keeps only
            // a faint hint of what is behind. Raise TintOpacity for less see-through, lower it for more.
            TintColor = light ? Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3) : Windows.UI.Color.FromArgb(255, 0x2C, 0x2C, 0x2C),
            TintOpacity = 0.55f,
            LuminosityOpacity = 0.85f,
        };
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(config);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }
}
