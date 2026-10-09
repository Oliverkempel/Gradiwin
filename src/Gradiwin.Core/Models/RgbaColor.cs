// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;

namespace Gradiwin.Core.Models;

/// <summary>Straight (non-premultiplied) color with channels in 0..1, mirroring Gdk.RGBA.</summary>
public readonly partial record struct RgbaColor(double R, double G, double B, double A = 1.0)
{
    public static readonly RgbaColor Black = new(0, 0, 0, 1);
    public static readonly RgbaColor White = new(1, 1, 1, 1);
    public static readonly RgbaColor Transparent = new(0, 0, 0, 0);

    public byte R8 => ToByte(R);
    public byte G8 => ToByte(G);
    public byte B8 => ToByte(B);
    public byte A8 => ToByte(A);

    public bool IsVisible => A > 0;

    /// <summary>Gradia's "any(c > 0 for c in rgba)" check.</summary>
    public bool HasAnyComponent => R > 0 || G > 0 || B > 0 || A > 0;

    public double Luminance => 0.299 * R + 0.587 * G + 0.114 * B;

    /// <summary>Port of is_light_color_rgba (transparent counts as light).</summary>
    public bool IsLight => A == 0 || Luminance * 255 > 130;

    public static RgbaColor FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255.0, g / 255.0, b / 255.0, a / 255.0);

    public RgbaColor WithAlpha(double alpha) => this with { A = alpha };

    /// <summary>"#rrggbb" (alpha dropped).</summary>
    public string ToHex() => $"#{R8:x2}{G8:x2}{B8:x2}";

    /// <summary>"rgb(r,g,b)" as used by Gradia gradient steps.</summary>
    public string ToRgbString() => $"rgb({R8},{G8},{B8})";

    /// <summary>Gradia ToolOption string format.</summary>
    public string ToRgbaString() => string.Create(CultureInfo.InvariantCulture, $"rgba({R:0.00}, {G:0.00}, {B:0.00}, {A:0.00})");

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);

    /// <summary>
    /// Parses "#rgb", "#rrggbb", "#aarrggbb" (Gradia preset style, alpha first), "rgb(r,g,b)" and "rgba(r,g,b,a)".
    /// </summary>
    public static RgbaColor Parse(string value)
    {
        if (TryParse(value, out var color))
            return color;
        throw new FormatException($"Invalid color: {value}");
    }

    public static bool TryParse(string? value, out RgbaColor color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var s = value.Trim();

        if (s.StartsWith('#'))
        {
            var hex = s[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return false;
            if (hex.Length == 6)
            {
                color = FromBytes((byte)(v >> 16), (byte)(v >> 8), (byte)v);
                return true;
            }
            if (hex.Length == 8)
            {
                color = FromBytes((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)(v >> 24));
                return true;
            }
            return false;
        }

        var m = FunctionRegex().Match(s);
        if (!m.Success)
            return false;
        var parts = m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;
        var nums = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i].TrimEnd('%');
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out nums[i]))
                return false;
        }

        if (m.Groups[1].Value.Equals("rgba", StringComparison.OrdinalIgnoreCase) && nums.Length >= 4 &&
            nums[0] <= 1 && nums[1] <= 1 && nums[2] <= 1 && s.Contains('.'))
        {
            // Gradia ToolOption style: rgba(0.65, 0.11, 0.18, 1.00) – components already 0..1.
            color = new RgbaColor(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }

        double a = nums.Length >= 4 ? nums[3] : 1.0;
        color = new RgbaColor(nums[0] / 255.0, nums[1] / 255.0, nums[2] / 255.0, a);
        return true;
    }

    [GeneratedRegex(@"^(rgba?)\s*\((.*)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionRegex();

    /// <summary>Linear interpolation, used when inserting gradient stops.</summary>
    public static RgbaColor Lerp(RgbaColor a, RgbaColor b, double t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
}
