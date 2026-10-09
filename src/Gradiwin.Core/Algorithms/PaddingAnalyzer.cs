// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Algorithms;

/// <summary>Port of LoadedImage._analyze_padding ("Auto Balance").</summary>
public static class PaddingAnalyzer
{
    /// <param name="bgra">Straight-alpha BGRA8 pixels, row-major, stride = width*4.</param>
    public static BalancedPadding Analyze(ReadOnlySpan<byte> bgra, int width, int height, int tolerance = 5)
    {
        if (width <= 0 || height <= 0)
            return new BalancedPadding(0, 0, 0, 0, RgbaColor.Transparent);

        byte rb = bgra[0], rg = bgra[1], rr = bgra[2], ra = bgra[3];

        bool Similar(ReadOnlySpan<byte> px, int x, int y)
        {
            int i = (y * width + x) * 4;
            return Math.Abs(px[i] - rb) <= tolerance &&
                   Math.Abs(px[i + 1] - rg) <= tolerance &&
                   Math.Abs(px[i + 2] - rr) <= tolerance &&
                   Math.Abs(px[i + 3] - ra) <= tolerance;
        }

        bool RowUniform(ReadOnlySpan<byte> px, int y)
        {
            for (int x = 0; x < width; x++)
                if (!Similar(px, x, y)) return false;
            return true;
        }

        bool ColumnUniform(ReadOnlySpan<byte> px, int x)
        {
            for (int y = 0; y < height; y++)
                if (!Similar(px, x, y)) return false;
            return true;
        }

        int top = height;
        for (int y = 0; y < height; y++)
            if (!RowUniform(bgra, y)) { top = y; break; }

        int bottom = height;
        for (int y = height - 1; y >= 0; y--)
            if (!RowUniform(bgra, y)) { bottom = height - 1 - y; break; }

        int left = width;
        for (int x = 0; x < width; x++)
            if (!ColumnUniform(bgra, x)) { left = x; break; }

        int right = width;
        for (int x = width - 1; x >= 0; x--)
            if (!ColumnUniform(bgra, x)) { right = width - 1 - x; break; }

        int max = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
        return new BalancedPadding(max - top, max - bottom, max - left, max - right, RgbaColor.FromBytes(rr, rg, rb, ra));
    }
}
