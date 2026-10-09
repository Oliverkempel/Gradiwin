// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Nodes;
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Annotations;

public enum DrawingMode
{
    Select,
    Pen,
    Text,
    Line,
    Arrow,
    Square,
    Circle,
    Highlighter,
    Censor,
    Number,
}

public static class DrawingModeExtensions
{
    public static string ToKey(this DrawingMode mode) => mode.ToString().ToUpperInvariant();

    public static DrawingMode FromKey(string? key) =>
        Enum.TryParse<DrawingMode>(key, ignoreCase: true, out var m) ? m : DrawingMode.Select;
}

/// <summary>Port of backend.tool_config.ToolOption. Serialization matches Gradia's JSON.</summary>
public sealed class ToolOptions
{
    public const string DefaultFont = "Segoe UI Variable Text";
    public const int MinSize = 3;
    public const int MaxSize = 25;

    private int _size = 14;
    private RgbaColor _primary = RgbaColor.Black;
    private RgbaColor _fill = RgbaColor.White;
    private RgbaColor _border = RgbaColor.Transparent;
    private string _font = DefaultFont;

    public ToolOptions(DrawingMode mode) => Mode = mode;

    public DrawingMode Mode { get; set; }

    /// <summary>Raised when a value changes (used to persist tool configuration).</summary>
    public event Action<ToolOptions>? Changed;

    public int Size
    {
        get => _size;
        set { if (_size != value) { _size = value; Changed?.Invoke(this); } }
    }

    public RgbaColor PrimaryColor
    {
        get => _primary;
        set { if (_primary != value) { _primary = value; Changed?.Invoke(this); } }
    }

    public RgbaColor FillColor
    {
        get => _fill;
        set { if (_fill != value) { _fill = value; Changed?.Invoke(this); } }
    }

    public RgbaColor BorderColor
    {
        get => _border;
        set { if (_border != value) { _border = value; Changed?.Invoke(this); } }
    }

    public string Font
    {
        get => _font;
        set { if (_font != value) { _font = value; Changed?.Invoke(this); } }
    }

    /// <summary>Copy without the change subscription (snapshot stored inside an annotation).</summary>
    public ToolOptions Clone() => new(Mode)
    {
        _size = _size,
        _primary = _primary,
        _fill = _fill,
        _border = _border,
        _font = _font,
    };

    public void CopyFrom(ToolOptions other, bool notify = true)
    {
        _size = other._size;
        _primary = other._primary;
        _fill = other._fill;
        _border = other._border;
        _font = other._font;
        if (notify) Changed?.Invoke(this);
    }

    private static JsonArray ColorToJson(RgbaColor c) => new(c.R, c.G, c.B, c.A);

    private static RgbaColor ColorFromJson(JsonNode? node, RgbaColor fallback)
    {
        if (node is JsonArray { Count: 4 } a)
            return new RgbaColor(a[0]!.GetValue<double>(), a[1]!.GetValue<double>(), a[2]!.GetValue<double>(), a[3]!.GetValue<double>());
        return fallback;
    }

    public string Serialize() => new JsonObject
    {
        ["mode"] = Mode.ToKey(),
        ["size"] = Size,
        ["primary_color"] = ColorToJson(PrimaryColor),
        ["fill_color"] = ColorToJson(FillColor),
        ["border_color"] = ColorToJson(BorderColor),
        ["font"] = Font,
    }.ToJsonString();

    public static ToolOptions Deserialize(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        var mode = DrawingModeExtensions.FromKey(node["mode"]?.GetValue<string>() ?? "PEN");
        return new ToolOptions(mode)
        {
            _size = node["size"]?.GetValue<int>() ?? 10,
            _primary = ColorFromJson(node["primary_color"], RgbaColor.Black),
            _fill = ColorFromJson(node["fill_color"], RgbaColor.Black),
            _border = ColorFromJson(node["border_color"], RgbaColor.Black),
            _font = node["font"]?.GetValue<string>() ?? DefaultFont,
        };
    }

    /// <summary>Defaults from Gradia's gschema "tool-config" default value.</summary>
    public static ToolOptions CreateDefault(DrawingMode mode)
    {
        var o = new ToolOptions(mode);
        switch (mode)
        {
            case DrawingMode.Highlighter:
                o._primary = new RgbaColor(0.96, 0.83, 0.18, 0.4);
                o._border = RgbaColor.Black;
                break;
            case DrawingMode.Number:
                o._primary = RgbaColor.Black;
                o._fill = RgbaColor.White;
                o._border = RgbaColor.Transparent;
                break;
            case DrawingMode.Circle:
            case DrawingMode.Square:
                o._primary = RgbaColor.White;
                o._fill = RgbaColor.White;
                o._border = RgbaColor.Black;
                break;
            case DrawingMode.Arrow:
            case DrawingMode.Line:
            case DrawingMode.Pen:
                o._border = RgbaColor.Black;
                break;
        }
        return o;
    }
}
