// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Algorithms;

/// <summary>
/// Exact port of gradia/graphics/gradient_gen.c plus the conic stop remapping done in
/// GradientBackground._generate_gradient_c. Produces opaque BGRA8 pixels (premultiplied == straight).
/// </summary>
public static class GradientGenerator
{
    private readonly record struct Stop(double Position, byte R, byte G, byte B);

    /// <summary>Returns the stops actually fed to the rasterizer (conic stops are rotated by the angle).</summary>
    public static IReadOnlyList<(double Position, RgbaColor Color)> PrepareStops(Gradient gradient)
    {
        var steps = gradient.Steps.Select(s => (s.Position, s.Rgba)).ToList();
        if (steps.Count == 0)
            steps = Gradient.DefaultSteps().Select(s => (s.Position, s.Rgba)).ToList();

        if (gradient.Mode == GradientMode.Conic)
        {
            double offset = Mod(gradient.Angle, 360.0) / 360.0;
            steps = steps.Select(s => (Mod(s.Position * 0.9 + 0.05 + offset, 1.0), s.Rgba)).ToList();
            steps.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            steps.Add((1.0, steps[0].Rgba));
        }
        return steps;
    }

    private static double Mod(double a, double m)
    {
        // Python modulo semantics (result has the sign of the divisor).
        double r = a % m;
        return r < 0 ? r + m : r;
    }

    public static byte[] GenerateBgra(Gradient gradient, int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        GenerateBgra(gradient, width, height, pixels);
        return pixels;
    }

    public static void GenerateBgra(Gradient gradient, int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
            return;

        var stops = PrepareStops(gradient)
            .Select(s => new Stop(s.Position, s.Color.R8, s.Color.G8, s.Color.B8))
            .ToArray();

        int mode = gradient.Mode switch
        {
            GradientMode.Conic => 1,
            GradientMode.Radial => 2,
            _ => 0,
        };
        double angle = gradient.Angle;
        double cosA = Math.Cos(angle * Math.PI / 180.0);
        double sinA = Math.Sin(angle * Math.PI / 180.0);

        double minCoord = 0.0, maxCoord = 1.0;
        if (mode == 0)
        {
            Span<double> corners =
            [
                0,
                (width - 1) * cosA,
                (height - 1) * sinA,
                (width - 1) * cosA + (height - 1) * sinA,
            ];
            minCoord = maxCoord = corners[0];
            for (int i = 1; i < 4; i++)
            {
                if (corners[i] < minCoord) minCoord = corners[i];
                if (corners[i] > maxCoord) maxCoord = corners[i];
            }
        }

        double range = maxCoord - minCoord;
        if (range == 0) range = 1.0;

        double cx = width / 2.0;
        double cy = height / 2.0;
        double maxRadius = Math.Sqrt(cx * cx + cy * cy);

        Parallel.For(0, height, y =>
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                double t;
                if (mode == 0)
                {
                    double coord = x * cosA + y * sinA;
                    t = (coord - minCoord) / range;
                }
                else if (mode == 1)
                {
                    double dx = x - cx, dy = y - cy;
                    t = (Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI);
                }
                else
                {
                    double dx = x - cx, dy = y - cy;
                    t = Math.Sqrt(dx * dx + dy * dy) / maxRadius;
                }

                if (t < 0) t = 0;
                if (t > 1) t = 1;

                Interpolate(t, stops, out byte r, out byte g, out byte b);
                int idx = row + x * 4;
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 255;
            }
        });
    }

    private static void Interpolate(double t, Stop[] stops, out byte r, out byte g, out byte b)
    {
        if (t <= stops[0].Position)
        {
            r = stops[0].R; g = stops[0].G; b = stops[0].B;
            return;
        }
        for (int i = 1; i < stops.Length; i++)
        {
            if (t <= stops[i].Position)
            {
                var p = stops[i - 1];
                var n = stops[i];
                double span = n.Position - p.Position;
                double ratio = span == 0 ? 0 : (t - p.Position) / span;
                // C truncates the double result when casting to uint8_t.
                r = (byte)(p.R + (n.R - p.R) * ratio);
                g = (byte)(p.G + (n.G - p.G) * ratio);
                b = (byte)(p.B + (n.B - p.B) * ratio);
                return;
            }
        }
        var last = stops[^1];
        r = last.R; g = last.G; b = last.B;
    }

    /// <summary>Color at a given position of the gradient's stop list (used by the stop editor).</summary>
    public static RgbaColor ColorAt(IReadOnlyList<GradientStop> steps, double t)
    {
        var sorted = steps.OrderBy(s => s.Position).ToList();
        if (sorted.Count == 0) return RgbaColor.Black;
        if (t <= sorted[0].Position) return sorted[0].Rgba;
        for (int i = 1; i < sorted.Count; i++)
        {
            if (t <= sorted[i].Position)
            {
                double span = sorted[i].Position - sorted[i - 1].Position;
                double ratio = span == 0 ? 0 : (t - sorted[i - 1].Position) / span;
                return RgbaColor.Lerp(sorted[i - 1].Rgba, sorted[i].Rgba, ratio);
            }
        }
        return sorted[^1].Rgba;
    }
}
