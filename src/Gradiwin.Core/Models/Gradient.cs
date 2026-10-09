// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gradiwin.Core.Models;

public enum GradientMode
{
    Linear,
    Conic,
    Radial,
}

/// <param name="Position">0..1</param>
/// <param name="Color">Color string, usually "rgb(r,g,b)" or "#rrggbb".</param>
public sealed record GradientStop(double Position, string Color)
{
    public RgbaColor Rgba => RgbaColor.TryParse(Color, out var c) ? c : RgbaColor.Black;
}

/// <summary>Port of gradia.graphics.gradient.Gradient. JSON is compatible with Gradia's "gradient-state".</summary>
public sealed class Gradient
{
    public GradientMode Mode { get; set; } = GradientMode.Linear;
    public List<GradientStop> Steps { get; set; } = DefaultSteps();
    public double Angle { get; set; } = 135.0;
    public string? Name { get; set; }

    public Gradient() { }

    public Gradient(GradientMode mode, IEnumerable<GradientStop> steps, double angle, string? name = null)
    {
        Mode = mode;
        Steps = steps.ToList();
        Angle = angle;
        Name = name;
    }

    public static List<GradientStop> DefaultSteps() =>
    [
        new(0.0, "rgb(87,227,137)"),
        new(1.0, "rgb(53,132,228)"),
    ];

    public Gradient Clone() => new(Mode, Steps.Select(s => s with { }), Angle, Name);

    public static string ModeToString(GradientMode mode) => mode switch
    {
        GradientMode.Conic => "conic",
        GradientMode.Radial => "radial",
        _ => "linear",
    };

    public static GradientMode ModeFromString(string? mode) => mode switch
    {
        "conic" => GradientMode.Conic,
        "radial" => GradientMode.Radial,
        _ => GradientMode.Linear,
    };

    public string ToJson()
    {
        var steps = new JsonArray();
        foreach (var s in Steps)
            steps.Add(new JsonArray(JsonValue.Create(s.Position), JsonValue.Create(s.Color)));
        var obj = new JsonObject
        {
            ["mode"] = ModeToString(Mode),
            ["steps"] = steps,
            ["angle"] = Angle,
        };
        return obj.ToJsonString();
    }

    public static Gradient FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Gradient();
        try
        {
            var node = JsonNode.Parse(json)!.AsObject();
            var g = new Gradient
            {
                Mode = ModeFromString(node["mode"]?.GetValue<string>()),
                Angle = node["angle"]?.GetValue<double>() ?? 135.0,
            };
            if (node["steps"] is JsonArray arr && arr.Count > 0)
            {
                g.Steps = arr.OfType<JsonArray>()
                    .Select(a => new GradientStop(a[0]!.GetValue<double>(), a[1]!.GetValue<string>()))
                    .OrderBy(s => s.Position)
                    .ToList();
            }
            if (g.Steps.Count == 0)
                g.Steps = DefaultSteps();
            return g;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or NullReferenceException)
        {
            return new Gradient();
        }
    }

    /// <summary>CSS representation (handy for debugging and previews).</summary>
    public string ToCss()
    {
        var steps = string.Join(", ", Steps.Select(s =>
            string.Create(CultureInfo.InvariantCulture, $"{s.Color} {s.Position * 100:0.00}%")));
        return Mode switch
        {
            GradientMode.Linear => string.Create(CultureInfo.InvariantCulture, $"linear-gradient({Angle:0.00}deg, {steps})"),
            GradientMode.Conic => string.Create(CultureInfo.InvariantCulture, $"conic-gradient(from {Angle:0.00}deg, {steps})"),
            GradientMode.Radial => $"radial-gradient(circle, {steps})",
            _ => "",
        };
    }

    public bool ContentEquals(Gradient other) =>
        Mode == other.Mode && Angle.Equals(other.Angle) && Steps.SequenceEqual(other.Steps);
}
