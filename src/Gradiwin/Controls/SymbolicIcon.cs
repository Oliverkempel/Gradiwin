// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Gradiwin.Controls;

/// <summary>
/// Draws one of Gradia's symbolic icons (converted to path layers by scripts/svg2icons.py)
/// in the current Foreground color, so icons follow light/dark theme like GNOME symbolic icons.
/// </summary>
public sealed partial class SymbolicIcon : UserControl
{
    private sealed record Layer(string D, bool Stroke, double Width, double Opacity, double[] Matrix, bool EvenOdd);
    private sealed record IconData(double W, double H, Layer[] Layers);

    private static Dictionary<string, IconData>? s_icons;

    public static readonly DependencyProperty NameKeyProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(SymbolicIcon), new PropertyMetadata(null, (d, _) => ((SymbolicIcon)d).Rebuild()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(SymbolicIcon), new PropertyMetadata(16.0, (d, _) => ((SymbolicIcon)d).Rebuild()));

    public SymbolicIcon()
    {
        IsTabStop = false;
        IsHitTestVisible = false;
    }

    public SymbolicIcon(string icon, double size = 16) : this()
    {
        Size = size;
        Icon = icon;
    }

    /// <summary>Icon name without the "-symbolic" suffix, e.g. "edit" or "crop".</summary>
    public string? Icon
    {
        get => (string?)GetValue(NameKeyProperty);
        set => SetValue(NameKeyProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static Dictionary<string, IconData> Icons
    {
        get
        {
            if (s_icons != null) return s_icons;
            s_icons = new Dictionary<string, IconData>(StringComparer.Ordinal);
            try
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "icons.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var size = prop.Value.GetProperty("size");
                    var layers = prop.Value.GetProperty("layers").EnumerateArray().Select(l => new Layer(
                        l.GetProperty("d").GetString() ?? "",
                        l.GetProperty("stroke").GetBoolean(),
                        l.GetProperty("width").GetDouble(),
                        l.GetProperty("opacity").GetDouble(),
                        l.GetProperty("matrix").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                        l.GetProperty("evenodd").GetBoolean())).ToArray();
                    s_icons[prop.Name] = new IconData(size[0].GetDouble(), size[1].GetDouble(), layers);
                }
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // Icons missing: render nothing.
            }
            return s_icons;
        }
    }

    public static bool Exists(string name) => Icons.ContainsKey(name);

    private void Rebuild()
    {
        Width = Size;
        Height = Size;
        if (Icon is null || !Icons.TryGetValue(Icon, out var data))
        {
            Content = null;
            return;
        }

        var canvas = new Canvas { Width = data.W, Height = data.H };
        foreach (var layer in data.Layers)
        {
            var geometry = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), (layer.EvenOdd ? "F0 " : "F1 ") + layer.D);
            var path = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Opacity = layer.Opacity,
                RenderTransform = new MatrixTransform
                {
                    Matrix = new Microsoft.UI.Xaml.Media.Matrix(layer.Matrix[0], layer.Matrix[1], layer.Matrix[2], layer.Matrix[3], layer.Matrix[4], layer.Matrix[5]),
                },
            };
            var brushBinding = new Microsoft.UI.Xaml.Data.Binding
            {
                Source = this,
                Path = new PropertyPath(nameof(Foreground)),
            };
            if (layer.Stroke)
            {
                path.StrokeThickness = layer.Width;
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
                path.StrokeLineJoin = PenLineJoin.Round;
                path.SetBinding(Shape.StrokeProperty, brushBinding);
            }
            else
            {
                path.SetBinding(Shape.FillProperty, brushBinding);
            }
            canvas.Children.Add(path);
        }
        Content = new Viewbox { Child = canvas, Width = Size, Height = Size, Stretch = Stretch.Uniform };
    }
}
