// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Models;
using Windows.UI;

namespace Gradiwin.Rendering;

public static class ColorUtil
{
    public static Color ToColor(RgbaColor c) => Color.FromArgb(c.A8, c.R8, c.G8, c.B8);

    public static RgbaColor ToRgba(Color c) => RgbaColor.FromBytes(c.R, c.G, c.B, c.A);

    /// <summary>Swatch color as Gradia shows translucent colors (blended over white).</summary>
    public static Color SwatchColor(RgbaColor c)
    {
        if (c.A <= 0) return Color.FromArgb(255, 0xb2, 0xb2, 0xb2);
        if (c.A >= 1) return ToColor(c);
        byte Blend(double v) => (byte)((v * c.A + (1 - c.A)) * 255);
        return Color.FromArgb(255, Blend(c.R), Blend(c.G), Blend(c.B));
    }
}
