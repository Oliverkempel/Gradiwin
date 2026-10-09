// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Algorithms;

/// <summary>Geometry of a processed image, computed exactly like ImageProcessor.process_to_pillow.</summary>
public sealed record ProcessLayout(
    int SourceWidth,
    int SourceHeight,
    int RotatedWidth,
    int RotatedHeight,
    BalancedPadding? Balance,
    (int X, int Y, int W, int H)? NegativeCrop,
    int ContentWidth,
    int ContentHeight,
    int CornerRadiusPixels,
    int FinalWidth,
    int FinalHeight,
    int PasteX,
    int PasteY,
    double ShadowBlurRadius,
    double ShadowAlpha);

public static class Layout
{
    public const int MaxPreviewPixels = 1024 * 1024;

    /// <summary>Port of LoadedImage._downscale_image dimension math.</summary>
    public static (int Width, int Height) PreviewSize(int width, int height)
    {
        long count = (long)width * height;
        if (count <= MaxPreviewPixels)
            return (width, height);
        double scale = Math.Sqrt(MaxPreviewPixels / (double)count);
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    /// <summary>Port of utils.aspect_ratio.parse_aspect_ratio. Returns null for empty/invalid.</summary>
    public static double? ParseAspectRatio(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        try
        {
            if (text.Contains(':'))
            {
                var parts = text.Split(':');
                if (parts.Length != 2) return null;
                double num = double.Parse(parts[0], CultureInfo.InvariantCulture);
                double den = double.Parse(parts[1], CultureInfo.InvariantCulture);
                if (den == 0) return null;
                return num / den;
            }
            return double.Parse(text, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static bool IsAspectRatioInBounds(double ratio, double min = 0.2, double max = 5) => ratio >= min && ratio <= max;

    /// <param name="scale">Full-resolution/preview scale; 1 for the preview pass.</param>
    public static ProcessLayout Compute(int width, int height, ImageOptions options, BalancedPadding? balance, double scale = 1.0)
    {
        int srcW = width, srcH = height;
        bool swap = options.Rotation is 90 or 270;
        int w = swap ? height : width;
        int h = swap ? width : height;
        int rotW = w, rotH = h;

        BalancedPadding? appliedBalance = null;
        if (options.AutoBalance && balance is { IsEmpty: false })
        {
            // Balance is measured on the unrotated preview; Gradia applies it after rotation as-is.
            appliedBalance = scale == 1.0
                ? balance
                : balance with
                {
                    Top = (int)(balance.Top * scale),
                    Bottom = (int)(balance.Bottom * scale),
                    Left = (int)(balance.Left * scale),
                    Right = (int)(balance.Right * scale),
                };
            w += appliedBalance.Left + appliedBalance.Right;
            h += appliedBalance.Top + appliedBalance.Bottom;
        }

        (int, int, int, int)? negCrop = null;
        if (options.Padding < 0)
        {
            int smaller = Math.Min(w, h);
            int pad = (int)(Math.Abs(options.Padding) / 100.0 * smaller);
            int cw = Math.Max(1, w - 2 * pad);
            int ch = Math.Max(1, h - 2 * pad);
            negCrop = ((w - cw) / 2, (h - ch) / 2, cw, ch);
            w = cw;
            h = ch;
        }

        int radius = options.CornerRadius > 0 ? (int)(options.CornerRadius / 100.0 * Math.Min(w, h)) : 0;

        var (finalW, finalH) = FinalDimensions(w, h, options.Padding, options.AspectRatio);

        int pasteX = 0, pasteY = 0;
        if (options.Padding >= 0)
        {
            pasteX = FloorDiv(finalW - w, 2);
            pasteY = FloorDiv(finalH - h, 2);
        }

        double strength = Math.Clamp(options.ShadowStrength, 0, 10) / 5.0;
        double blur = (int)(10 * strength * scale);
        // Gradia builds the shadow with color (0,0,0,150*s) but then calls putalpha() with the image's
        // alpha channel, so the silhouette is effectively opaque black; only the blur depends on strength.
        double alpha = strength > 0 ? 1.0 : 0.0;

        return new ProcessLayout(srcW, srcH, rotW, rotH, appliedBalance, negCrop, w, h, radius,
            finalW, finalH, pasteX, pasteY, blur, alpha);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

    /// <summary>Port of _calculate_final_dimensions + _adjust_for_aspect_ratio.</summary>
    public static (int Width, int Height) FinalDimensions(int width, int height, int padding, string? aspectRatio)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (padding >= 0)
        {
            int smaller = Math.Min(width, height);
            int pad = (int)(padding / 100.0 * smaller);
            width += pad * 2;
            height += pad * 2;
        }

        var ratio = ParseAspectRatio(aspectRatio);
        if (ratio is > 0)
        {
            double current = width / (double)height;
            if (current < ratio)
                width = (int)(height * ratio.Value);
            else if (current > ratio)
                height = (int)(width / ratio.Value);
        }
        return (width, height);
    }

    /// <summary>Image-cover placement used by ImageBackground.prepare_image.</summary>
    public static (double X, double Y, double W, double H) CoverRect(int imageW, int imageH, int targetW, int targetH)
    {
        double imgRatio = imageW / (double)imageH;
        double targetRatio = targetW / (double)targetH;
        int newW, newH;
        if (imgRatio > targetRatio)
        {
            newH = targetH;
            newW = (int)(newH * imgRatio);
        }
        else
        {
            newW = targetW;
            newH = (int)(newW / imgRatio);
        }
        int left = (newW - targetW) / 2;
        int top = (newH - targetH) / 2;
        return (-left, -top, newW, newH);
    }
}
