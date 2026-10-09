// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Nodes;

namespace Gradiwin.Core.Models;

public enum BackgroundMode
{
    None,
    Solid,
    Gradient,
    Image,
}

public static class BackgroundModeExtensions
{
    public static string ToKey(this BackgroundMode mode) => mode switch
    {
        BackgroundMode.None => "none",
        BackgroundMode.Solid => "solid",
        BackgroundMode.Image => "image",
        _ => "gradient",
    };

    public static BackgroundMode FromKey(string? key) => key switch
    {
        "none" => BackgroundMode.None,
        "solid" => BackgroundMode.Solid,
        "image" => BackgroundMode.Image,
        _ => BackgroundMode.Gradient,
    };
}

/// <summary>Port of SolidBackground; JSON compatible with "solid-state".</summary>
public sealed record SolidBackground(string Color = "#4A90E2", double Alpha = 1.0)
{
    public RgbaColor Rgba => RgbaColor.Parse(Color).WithAlpha(Alpha);

    public string ToJson() => new JsonObject
    {
        ["type"] = "solid",
        ["color"] = Color,
        ["alpha"] = Alpha,
    }.ToJsonString();

    public static SolidBackground FromJson(string? json)
    {
        try
        {
            var node = JsonNode.Parse(json ?? "")!.AsObject();
            return new SolidBackground(
                node["color"]?.GetValue<string>() ?? "#4A90E2",
                node["alpha"]?.GetValue<double>() ?? 1.0);
        }
        catch (Exception)
        {
            return new SolidBackground();
        }
    }
}

/// <summary>State of the image background (path to file or "preset:name").</summary>
public sealed record ImageBackgroundState(string? Path)
{
    public string ToJson() => new JsonObject { ["path"] = Path }.ToJsonString();

    public static ImageBackgroundState FromJson(string? json)
    {
        try
        {
            var node = JsonNode.Parse(json ?? "")!.AsObject();
            return new ImageBackgroundState(node["path"]?.GetValue<string>());
        }
        catch (Exception)
        {
            return new ImageBackgroundState((string?)null);
        }
    }
}
