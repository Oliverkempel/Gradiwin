// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;

namespace Gradiwin.Rendering;

/// <summary>
/// Maps image coordinates (centered on the processed preview) to drawing-session coordinates.
/// <paramref name="Scale"/> is output units per preview pixel (Gradia's "scale" argument).
/// </summary>
public readonly record struct AnnotationTransform(Vector2 Origin, float PreviewWidth, float PreviewHeight, float Scale)
{
    public Vector2 Map(PointD p) =>
        Origin + new Vector2((float)((PreviewWidth / 2 + p.X) * Scale), (float)((PreviewHeight / 2 + p.Y) * Scale));

    public Vector2 Map(double x, double y) => Map(new PointD(x, y));

    public PointD Unmap(Vector2 v) =>
        new((v.X - Origin.X) / Scale - PreviewWidth / 2, (v.Y - Origin.Y) / Scale - PreviewHeight / 2);
}

/// <summary>Win2D port of the Cairo drawing code in overlay/drawing_actions.py.</summary>
public static class AnnotationRenderer
{
    /// <param name="processed">Processed image used by the censor tool (same image the annotations sit on).</param>
    /// <param name="processedScale">Pixels of <paramref name="processed"/> per preview pixel.</param>
    public static void Draw(CanvasDrawingSession ds, IEnumerable<Annotation> annotations, AnnotationTransform t,
        ICanvasImage? processed, float processedScale)
    {
        foreach (var a in annotations)
            Draw(ds, a, t, processed, processedScale);
    }

    public static void Draw(CanvasDrawingSession ds, Annotation a, AnnotationTransform t, ICanvasImage? processed, float processedScale)
    {
        switch (a)
        {
            case HighlighterAnnotation h: DrawHighlighter(ds, h, t); break;
            case StrokeAnnotation s: DrawStroke(ds, s, t); break;
            case ArrowAnnotation arrow: DrawArrow(ds, arrow, t); break;
            case LineAnnotation line: DrawLine(ds, line, t); break;
            case CensorAnnotation censor: DrawCensor(ds, censor, t, processed, processedScale); break;
            case EllipseAnnotation e: DrawEllipse(ds, e, t); break;
            case RectAnnotation r: DrawRect(ds, r, t); break;
            case TextAnnotation text: DrawText(ds, text, t); break;
            case NumberAnnotation n: DrawNumber(ds, n, t); break;
        }
    }

    private static Windows.UI.Color C(RgbaColor c) => ColorUtil.ToColor(c);

    private static readonly CanvasStrokeStyle RoundStroke = new()
    {
        StartCap = CanvasCapStyle.Round,
        EndCap = CanvasCapStyle.Round,
        DashCap = CanvasCapStyle.Round,
        LineJoin = CanvasLineJoin.Round,
    };

    private static readonly CanvasStrokeStyle ButtStroke = new()
    {
        StartCap = CanvasCapStyle.Flat,
        EndCap = CanvasCapStyle.Flat,
        LineJoin = CanvasLineJoin.Round,
    };

    private static readonly CanvasStrokeStyle MiterStroke = new() { LineJoin = CanvasLineJoin.Miter };

    private static void DrawStroke(CanvasDrawingSession ds, StrokeAnnotation s, AnnotationTransform t)
    {
        if (s.Points.Count < 2) return;
        var pts = s.Points.Select(t.Map).ToList();
        using var pb = new CanvasPathBuilder(ds);
        pb.BeginFigure(pts[0]);
        if (pts.Count == 2)
        {
            pb.AddLine(pts[1]);
        }
        else
        {
            // Gradia: curve_to(x1,y1,x1,y1,mid) for every interior point, then line to the last.
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var mid = (pts[i] + pts[i + 1]) * 0.5f;
                pb.AddCubicBezier(pts[i], pts[i], mid);
            }
            pb.AddLine(pts[^1]);
        }
        pb.EndFigure(CanvasFigureLoop.Open);
        using var geo = CanvasGeometry.CreatePath(pb);
        ds.DrawGeometry(geo, C(s.Options.PrimaryColor), (float)(s.Options.Size * t.Scale), RoundStroke);
    }

    private static void DrawHighlighter(CanvasDrawingSession ds, HighlighterAnnotation h, AnnotationTransform t)
    {
        if (h.Points.Count < 2) return;
        using var pb = new CanvasPathBuilder(ds);
        pb.BeginFigure(t.Map(h.Points[0]));
        foreach (var p in h.Points.Skip(1))
            pb.AddLine(t.Map(p));
        pb.EndFigure(CanvasFigureLoop.Open);
        using var geo = CanvasGeometry.CreatePath(pb);
        // Gradia uses OPERATOR_MULTIPLY on a transparent overlay surface, which composites like a translucent stroke.
        ds.DrawGeometry(geo, C(h.Options.PrimaryColor), (float)(h.Options.Size * t.Scale * 2), ButtStroke);
    }

    private static void DrawLine(CanvasDrawingSession ds, LineAnnotation l, AnnotationTransform t)
    {
        var s = t.Map(l.Start);
        var e = t.Map(l.End);
        float width = (float)(l.StrokeWidth * t.Scale);
        float angle = MathF.Atan2(e.Y - s.Y, e.X - s.X);
        var d = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (width / 2);
        // Gradia shortens the segment by half the width (cairo default butt caps).
        ds.DrawLine(s + d, e - d, C(l.Options.PrimaryColor), width, ButtStroke);
    }

    private static void DrawArrow(CanvasDrawingSession ds, ArrowAnnotation a, AnnotationTransform t)
    {
        var s = t.Map(a.Start);
        var e = t.Map(a.End);
        float distance = Vector2.Distance(s, e);
        if (distance < ArrowAnnotation.MinDistance) return;

        float width = (float)(a.StrokeWidth * t.Scale);
        double scale = ArrowAnnotation.HeadScale(distance, ArrowAnnotation.ScaleDistanceThreshold * t.Scale);
        float head = (float)(a.HeadSize * scale * t.Scale);
        double angle = Math.Atan2(e.Y - s.Y, e.X - s.X);
        var color = C(a.Options.PrimaryColor);

        ds.DrawLine(s, e, color, width, RoundStroke);
        foreach (var side in new[] { angle + Math.PI - ArrowAnnotation.HeadAngle, angle + Math.PI + ArrowAnnotation.HeadAngle })
        {
            var p = new Vector2(e.X + head * (float)Math.Cos(side), e.Y + head * (float)Math.Sin(side));
            ds.DrawLine(e, p, color, width, RoundStroke);
        }
    }

    private static (float X, float Y, float W, float H)? InsetRect(RectAnnotation r, AnnotationTransform t)
    {
        var a = t.Map(r.Start);
        var b = t.Map(r.EffectiveEnd);
        float stroke = (float)(r.Options.Size * t.Scale);
        float x = MathF.Min(a.X, b.X) + stroke / 2;
        float y = MathF.Min(a.Y, b.Y) + stroke / 2;
        float w = MathF.Abs(b.X - a.X) - stroke;
        float h = MathF.Abs(b.Y - a.Y) - stroke;
        return w > 0 && h > 0 ? (x, y, w, h) : null;
    }

    private static void DrawRect(CanvasDrawingSession ds, RectAnnotation r, AnnotationTransform t)
    {
        if (InsetRect(r, t) is not { } rc) return;
        var rect = new Rect(rc.X, rc.Y, rc.W, rc.H);
        if (r.Options.FillColor.A > 0)
            ds.FillRectangle(rect, C(r.Options.FillColor));
        ds.DrawRectangle(rect, C(r.Options.PrimaryColor), (float)(r.Options.Size * t.Scale), MiterStroke);
    }

    private static void DrawEllipse(CanvasDrawingSession ds, EllipseAnnotation e, AnnotationTransform t)
    {
        if (InsetRect(e, t) is not { } rc) return;
        var center = new Vector2(rc.X + rc.W / 2, rc.Y + rc.H / 2);
        if (e.Options.FillColor.A > 0)
            ds.FillEllipse(center, rc.W / 2, rc.H / 2, C(e.Options.FillColor));
        ds.DrawEllipse(center, rc.W / 2, rc.H / 2, C(e.Options.PrimaryColor), (float)(e.Options.Size * t.Scale));
    }

    private static void DrawCensor(CanvasDrawingSession ds, CensorAnnotation c, AnnotationTransform t, ICanvasImage? processed, float processedScale)
    {
        if (processed is null) return;
        float pw = t.PreviewWidth, ph = t.PreviewHeight;

        // Crop in preview pixels (Gradia: _get_image_crop), clamped to the image.
        double x1 = Math.Clamp(c.Start.X + pw / 2, 0, pw), x2 = Math.Clamp(c.End.X + pw / 2, 0, pw);
        double y1 = Math.Clamp(c.Start.Y + ph / 2, 0, ph), y2 = Math.Clamp(c.End.Y + ph / 2, 0, ph);
        int cx = (int)Math.Min(x1, x2), cy = (int)Math.Min(y1, y2);
        int cw = (int)Math.Max(x1, x2) - cx, ch = (int)Math.Max(y1, y2) - cy;
        if (cw <= 0 || ch <= 0) return;

        double block = c.BlockSizeImage;
        int bx = Math.Max(1, (int)(cw / block));
        int by = Math.Max(1, (int)(ch / block));

        // Source rectangle in the processed image's own pixels.
        float sx = cx * processedScale, sy = cy * processedScale, sw = cw * processedScale, sh = ch * processedScale;

        var cropped = new CropEffect
        {
            Source = processed,
            SourceRectangle = new Rect(sx, sy, sw, sh),
            BorderMode = EffectBorderMode.Hard,
        };
        var atOrigin = new Transform2DEffect
        {
            Source = cropped,
            TransformMatrix = Matrix3x2.CreateTranslation(-sx, -sy),
            BorderMode = EffectBorderMode.Hard,
        };
        var down = new ScaleEffect
        {
            Source = atOrigin,
            Scale = new Vector2(bx / sw, by / sh),
            InterpolationMode = CanvasImageInterpolation.HighQualityCubic,
            BorderMode = EffectBorderMode.Hard,
        };
        // Snap to the block grid so every block is a solid color.
        var grid = new CropEffect { Source = down, SourceRectangle = new Rect(0, 0, bx, by) };

        var topLeft = t.Map(cx - pw / 2.0, cy - ph / 2.0);
        float destW = cw * t.Scale, destH = ch * t.Scale;
        var up = new Transform2DEffect
        {
            Source = grid,
            TransformMatrix = Matrix3x2.CreateScale(destW / bx, destH / by) * Matrix3x2.CreateTranslation(topLeft),
            InterpolationMode = CanvasImageInterpolation.NearestNeighbor,
        };
        ds.DrawImage(up);
    }

    private static CanvasTextFormat TextFormat(string font, float sizePx, bool bold = false) => new()
    {
        FontFamily = font,
        FontSize = Math.Max(1f, sizePx),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
        HorizontalAlignment = CanvasHorizontalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
        Options = CanvasDrawTextOptions.EnableColorFont,
    };

    private static void DrawText(CanvasDrawingSession ds, TextAnnotation a, AnnotationTransform t)
    {
        if (string.IsNullOrWhiteSpace(a.Text)) return;
        var anchor = t.Map(a.Position);
        float sizePx = (float)(TextAnnotation.PointsToPixels(a.FontSize) * t.Scale);

        using var format = TextFormat(a.Options.Font, sizePx);
        using var layout = new CanvasTextLayout(ds, a.Text, format, 0, 0);
        var bounds = layout.LayoutBounds;
        // Anchor is bottom-center of the logical text rectangle.
        var textTopLeft = new Vector2(anchor.X - (float)bounds.Width / 2, anchor.Y - (float)bounds.Height);
        var drawOffset = textTopLeft - new Vector2((float)bounds.X, (float)bounds.Y);

        if (a.HasBackground)
            DrawTextBackground(ds, a, format, textTopLeft, (float)bounds.Width, t.Scale);

        var primary = C(a.Options.PrimaryColor);
        if (a.ContainsEmoji())
        {
            ds.DrawTextLayout(layout, drawOffset, primary);
            return;
        }

        using var geo = CanvasGeometry.CreateText(layout).Transform(Matrix3x2.CreateTranslation(drawOffset));
        if (a.HasOutline)
        {
            float outline = (float)(2.0 * t.Scale * (a.FontSize / 14.0));
            ds.DrawGeometry(geo, C(a.Options.BorderColor), outline, RoundStroke);
        }
        ds.FillGeometry(geo, primary);
    }

    /// <summary>Port of TextAction.draw_per_line_background (rounded per-line pills that merge).</summary>
    private static void DrawTextBackground(CanvasDrawingSession ds, TextAnnotation a, CanvasTextFormat format, Vector2 textTopLeft, float overallWidth, float scale)
    {
        var lines = a.Text.Split('\n');
        var fill = C(a.Options.FillColor);
        float padX = (float)(TextAnnotation.PaddingX * scale), padY = (float)(TextAnnotation.PaddingY * scale);

        var sizes = lines.Select(line =>
        {
            using var l = new CanvasTextLayout(ds, line.Length == 0 ? " " : line, format, 0, 0);
            return (W: line.Length == 0 ? 0f : (float)l.LayoutBounds.Width, H: (float)l.LayoutBounds.Height);
        }).ToList();

        float y = textTopLeft.Y;
        for (int i = 0; i < sizes.Count; i++)
        {
            var (w, h) = sizes[i];
            float bgX = textTopLeft.X + (overallWidth - w) / 2 - padX;
            float bgY = y - padY;
            float bgW = w + 2 * padX, bgH = h + 2 * padY;
            float radius = MathF.Min(6f * scale, MathF.Min(bgW, bgH) / 4);
            bool roundTop = !(i > 0 && sizes[i - 1].W >= w);
            bool roundBottom = !(i < sizes.Count - 1 && sizes[i + 1].W >= w);
            if (sizes.Count == 1) roundTop = roundBottom = true;

            using var geo = RoundedRect(ds, bgX, bgY, bgW, bgH, radius, roundTop, roundBottom);
            ds.FillGeometry(geo, fill);
            y += h;
        }
    }

    private static CanvasGeometry RoundedRect(ICanvasResourceCreator rc, float x, float y, float w, float h, float r, bool top, bool bottom)
    {
        using var pb = new CanvasPathBuilder(rc);
        if (top)
        {
            pb.BeginFigure(x, y + r);
            pb.AddArc(new Vector2(x + r, y), r, r, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
            pb.AddLine(x + w - r, y);
            pb.AddArc(new Vector2(x + w, y + r), r, r, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
        }
        else
        {
            pb.BeginFigure(x, y);
            pb.AddLine(x + w, y);
        }
        if (bottom)
        {
            pb.AddLine(x + w, y + h - r);
            pb.AddArc(new Vector2(x + w - r, y + h), r, r, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
            pb.AddLine(x + r, y + h);
            pb.AddArc(new Vector2(x, y + h - r), r, r, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
        }
        else
        {
            pb.AddLine(x + w, y + h);
            pb.AddLine(x, y + h);
        }
        pb.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(pb);
    }

    private static void DrawNumber(CanvasDrawingSession ds, NumberAnnotation n, AnnotationTransform t)
    {
        var center = t.Map(n.Position);
        float r = (float)(n.Radius * t.Scale);
        var o = n.Options;

        ds.FillCircle(center, r, C(o.FillColor));
        if (o.BorderColor.A != 0 && o.FillColor.A != 0)
            ds.DrawCircle(center, r, C(o.BorderColor), 2f * t.Scale);

        using var format = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Display",
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = Math.Max(1f, r * 1.2f),
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        using var layout = new CanvasTextLayout(ds, n.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), format, 0, 0);
        using var raw = CanvasGeometry.CreateText(layout);
        var ink = raw.ComputeBounds();
        var offset = center - new Vector2((float)(ink.X + ink.Width / 2), (float)(ink.Y + ink.Height / 2));
        using var geo = raw.Transform(Matrix3x2.CreateTranslation(offset));

        if (o.BorderColor.HasAnyComponent && o.BorderColor.A > 0)
            ds.DrawGeometry(geo, C(o.BorderColor), 4f * t.Scale, RoundStroke);
        ds.FillGeometry(geo, C(o.PrimaryColor));
    }

    /// <summary>DirectWrite-backed measurement for <see cref="TextAnnotation"/> bounds.</summary>
    public sealed class Win2DTextMeasurer : ITextMeasurer
    {
        public (double Width, double Height, IReadOnlyList<(double Width, double Height)> Lines) Measure(string text, string font, double fontSizePx, bool bold = false)
        {
            var device = CanvasDevice.GetSharedDevice();
            using var format = TextFormat(font, (float)fontSizePx, bold);
            using var layout = new CanvasTextLayout(device, text, format, 0, 0);
            var lines = layout.LineMetrics.Select(m => (layout.LayoutBounds.Width, (double)m.Height)).ToList();
            return (layout.LayoutBounds.Width, layout.LayoutBounds.Height, lines);
        }
    }
}
