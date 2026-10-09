// SPDX-License-Identifier: GPL-3.0-or-later
namespace Gradiwin.Core.Models;

/// <summary>Image options from the sidebar (port of ImageOptions / ImageProcessor parameters).</summary>
public sealed record ImageOptions
{
    /// <summary>Percent of the smaller dimension. Negative values crop inward.</summary>
    public int Padding { get; init; } = 5;

    /// <summary>Percent of the smaller dimension.</summary>
    public int CornerRadius { get; init; } = 2;

    /// <summary>"" for automatic, else "W:H" or a decimal ratio.</summary>
    public string AspectRatio { get; init; } = "";

    /// <summary>0..10.</summary>
    public double ShadowStrength { get; init; } = 5;

    public bool AutoBalance { get; init; }

    /// <summary>0, 90, 180 or 270 (counter-clockwise like PIL ROTATE_x).</summary>
    public int Rotation { get; init; }

    /// <summary>When the background is "none" Gradia forces these to zero.</summary>
    public ImageOptions ForNoBackground() => this with
    {
        Padding = 0,
        CornerRadius = 0,
        AspectRatio = "",
        ShadowStrength = 0,
    };
}

/// <summary>Port of gradia.graphics.loaded_image.BalancedPadding.</summary>
public sealed record BalancedPadding(int Top, int Bottom, int Left, int Right, RgbaColor Color)
{
    public int MaxPadding => Math.Max(Math.Max(Top, Bottom), Math.Max(Left, Right));
    public int TotalHorizontal => Left + Right;
    public int TotalVertical => Top + Bottom;
    public bool IsEmpty => Top == 0 && Bottom == 0 && Left == 0 && Right == 0;
}

/// <summary>Normalized (0..1) crop rectangle over the final composed image.</summary>
public readonly record struct CropRect(double X, double Y, double Width, double Height)
{
    public static readonly CropRect Full = new(0, 0, 1, 1);
    public static readonly CropRect Initial = new(0.1, 0.1, 0.8, 0.8);
    public bool IsFull => X <= 0 && Y <= 0 && Width >= 1 && Height >= 1;

    public (int X, int Y, int W, int H) ToPixels(int width, int height)
    {
        int x = (int)Math.Round(X * width);
        int y = (int)Math.Round(Y * height);
        int w = Math.Max(1, (int)Math.Round(Width * width));
        int h = Math.Max(1, (int)Math.Round(Height * height));
        w = Math.Min(w, width - x);
        h = Math.Min(h, height - y);
        return (x, y, Math.Max(1, w), Math.Max(1, h));
    }
}

public enum ImageOrigin
{
    FileDialog,
    DragDrop,
    Clipboard,
    Screenshot,
    FakeScreenshot,
    CommandLine,
    SourceImage,
}
