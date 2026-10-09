// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Presets;

/// <summary>Named color; <see cref="Name"/> is the English msgid used for translation lookup.</summary>
public sealed record NamedColor(RgbaColor Color, string Name);

/// <summary>Values ported from app_constants.py, solid.py, tool_config.py and quick_color_picker.py.</summary>
public static class Palettes
{
    public static readonly IReadOnlyList<Gradient> PredefinedGradients =
    [
        new(GradientMode.Linear, [new(0.0, "#f66151"), new(1.0, "#ed333b")], 45, "Red Flame"),
        new(GradientMode.Linear, [new(0.0, "#ff5f6d"), new(1.0, "#ffc371")], 45, "Sunset Orange"),
        new(GradientMode.Linear, [new(0.0, "#ffd200"), new(1.0, "#f7971e")], 135, "Golden Hour"),
        new(GradientMode.Linear, [new(0.0, "#DFFFCD"), new(0.5, "#90F9C4"), new(1.0, "#39F3BB")], 135, "Mint Fresh"),
        new(GradientMode.Linear, [new(0.0, "#57e389"), new(1.0, "#3584e4")], 135, "Ocean Breeze"),
        new(GradientMode.Linear, [new(0.0, "#23d4fd"), new(0.5, "#3a98f0"), new(1.0, "#b721ff")], 45, "Purple Dream"),
    ];

    /// <summary>Solid background presets (#AARRGGBB, name).</summary>
    public static readonly IReadOnlyList<NamedColor> SolidColors =
    [
        new(RgbaColor.Parse("#fff66151"), "Red"),
        new(RgbaColor.Parse("#ffe66100"), "Orange"),
        new(RgbaColor.Parse("#fff6d32d"), "Yellow"),
        new(RgbaColor.Parse("#ff33d17a"), "Green"),
        new(RgbaColor.Parse("#ff3584e4"), "Blue"),
        new(RgbaColor.Parse("#ffc061cb"), "Purple"),
        new(RgbaColor.Parse("#ffffd1dc"), "Pastel Pink"),
        new(RgbaColor.Parse("#fffff4d1"), "Pastel Yellow"),
        new(RgbaColor.Parse("#ffd1ffd1"), "Pastel Green"),
        new(RgbaColor.Parse("#ffd1f0ff"), "Pastel Cyan"),
        new(RgbaColor.Parse("#ffffffff"), "White"),
        new(RgbaColor.Parse("#ff000000"), "Black"),
        new(RgbaColor.Parse("#ff77767b"), "Gray"),
        new(RgbaColor.Parse("#00000000"), "Transparent"),
    ];

    public static IReadOnlyList<NamedColor> BaseColors(double alpha = 1.0, bool secondary = false)
    {
        var list = new List<NamedColor>
        {
            new(new RgbaColor(0.88, 0.11, 0.14, alpha), "Red"),
            new(new RgbaColor(0.18, 0.76, 0.49, alpha), "Green"),
            new(new RgbaColor(0.21, 0.52, 0.89, alpha), "Blue"),
            new(new RgbaColor(0.96, 0.83, 0.18, alpha), "Yellow"),
            new(new RgbaColor(0.0, 0.0, 0.0, alpha), "Black"),
            new(new RgbaColor(1.0, 1.0, 1.0, alpha), "White"),
        };
        if (secondary)
            list.Add(new(RgbaColor.Transparent, "Transparent"));
        return list;
    }

    public static readonly IReadOnlyList<NamedColor> TextColors =
    [
        new(new RgbaColor(0.65, 0.11, 0.18), "Red"),
        new(new RgbaColor(0.15, 0.64, 0.41), "Green"),
        new(new RgbaColor(0.1, 0.37, 0.71), "Blue"),
        new(new RgbaColor(0.9, 0.65, 0.04), "Yellow"),
        new(new RgbaColor(0, 0, 0), "Black"),
        new(new RgbaColor(1, 1, 1), "White"),
    ];

    public static readonly IReadOnlyList<NamedColor> TextBackgroundColors =
    [
        new(new RgbaColor(0.96, 0.38, 0.32), "Red"),
        new(new RgbaColor(0.56, 0.94, 0.64), "Green"),
        new(new RgbaColor(0.6, 0.76, 0.95), "Blue"),
        new(new RgbaColor(0.98, 0.94, 0.42), "Yellow"),
        new(new RgbaColor(1, 1, 1), "White"),
        new(new RgbaColor(0, 0, 0), "Black"),
        new(RgbaColor.Transparent, "Transparent"),
    ];

    public static readonly IReadOnlyList<NamedColor> HighlighterColors =
    [
        new(new RgbaColor(0.88, 0.11, 0.14, 0.4), "Red"),
        new(new RgbaColor(0.18, 0.76, 0.49, 0.4), "Green"),
        new(new RgbaColor(0.21, 0.52, 0.89, 0.4), "Blue"),
        new(new RgbaColor(0.96, 0.83, 0.18, 0.4), "Yellow"),
        new(new RgbaColor(0.51, 0.24, 0.61, 0.4), "Purple"),
        new(new RgbaColor(1.0, 1.0, 1.0, 0.4), "White"),
    ];

    /// <summary>Background image presets shipped in Assets/Backgrounds.</summary>
    public static readonly IReadOnlyList<string> PresetImages =
    [
        "adwaita.webp", "blobs.webp", "coral.webp", "drool.webp", "graph.png",
        "mesh.webp", "pink.webp", "sky.webp", "swirl.webp",
    ];

    /// <summary>Background aspect ratio presets (label, value).</summary>
    public static readonly IReadOnlyList<(string Label, string Value)> BackgroundAspectRatios =
    [
        ("Auto", ""), ("1:1", "1:1"), ("16:9", "16:9"), ("4:3", "4:3"), ("3:2", "3:2"),
        ("2:3", "2:3"), ("3:4", "3:4"), ("9:16", "9:16"), ("21:9", "21:9"),
    ];

    /// <summary>Crop aspect-lock presets (label msgid, ratio or null for free).</summary>
    public static readonly IReadOnlyList<(string Label, double? Ratio, string Icon)> CropAspectRatios =
    [
        ("Free", null, "aspect-ratio-free"),
        ("Square", 1.0, "aspect-ratio-square"),
        ("4:3", 4.0 / 3.0, "aspect-ratio-4to3"),
        ("3:2", 3.0 / 2.0, "aspect-ratio-3to2"),
        ("5:4", 5.0 / 4.0, "aspect-ratio-5to4"),
        ("16:9", 16.0 / 9.0, "aspect-ratio-16to9"),
    ];
}

/// <summary>Which option panel a tool shows (Gradia's extra_stack page).</summary>
public enum ToolPanel
{
    Empty,
    Text,
    Fill,
    FillBorder,
}

/// <summary>Port of tool_config.ToolConfig.</summary>
public sealed record ToolDefinition(
    DrawingMode Mode,
    string Label,
    string Icon,
    int Column,
    int Row,
    ToolPanel Panel,
    bool HasScale,
    bool HasPrimaryColor,
    bool MatchPrimaryToSecondary,
    IReadOnlyList<NamedColor> PrimaryColors,
    IReadOnlyList<NamedColor> SecondaryColors,
    IReadOnlyList<string> Shortcuts)
{
    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new(DrawingMode.Select, "Select", "pointer-primary-click", 0, 0, ToolPanel.Empty, false, false, false,
            Palettes.BaseColors(), Palettes.BaseColors(secondary: true), ["0", "`", "s"]),
        new(DrawingMode.Pen, "Pen", "edit", 1, 0, ToolPanel.Empty, true, true, false,
            Palettes.BaseColors(), Palettes.BaseColors(secondary: true), ["1", "d", "p"]),
        new(DrawingMode.Text, "Text", "text-insert2", 2, 0, ToolPanel.Text, false, true, false,
            Palettes.TextColors, Palettes.TextBackgroundColors, ["2", "t"]),
        new(DrawingMode.Line, "Line", "draw-line", 3, 0, ToolPanel.Empty, true, true, false,
            Palettes.BaseColors(), Palettes.BaseColors(secondary: true), ["3", "l"]),
        new(DrawingMode.Arrow, "Arrow", "arrow1-top-right", 4, 0, ToolPanel.Empty, true, true, false,
            Palettes.BaseColors(), Palettes.BaseColors(secondary: true), ["4", "a"]),
        new(DrawingMode.Square, "Rectangle", "box-small-outline", 0, 1, ToolPanel.Fill, true, true, false,
            Palettes.BaseColors(), Palettes.BaseColors(secondary: true), ["5", "r"]),
        new(DrawingMode.Circle, "Oval", "circle-outline-thick", 1, 1, ToolPanel.Fill, true, true, false,
            Palettes.BaseColors(), Palettes.BaseColors(secondary: true), ["6", "o"]),
        new(DrawingMode.Highlighter, "Highlighter", "marker", 2, 1, ToolPanel.Empty, true, true, false,
            Palettes.HighlighterColors, Palettes.BaseColors(secondary: true), ["7", "h"]),
        new(DrawingMode.Censor, "Censor", "checkerboard-big", 3, 1, ToolPanel.Empty, false, false, false,
            Palettes.BaseColors(), Palettes.BaseColors(secondary: true), ["8", "c"]),
        new(DrawingMode.Number, "Number", "one-circle", 4, 1, ToolPanel.FillBorder, true, true, true,
            Palettes.TextColors, Palettes.TextBackgroundColors, ["9", "n"]),
    ];

    public static ToolDefinition For(DrawingMode mode) => All.First(t => t.Mode == mode);
}
