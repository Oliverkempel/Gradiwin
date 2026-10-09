// SPDX-License-Identifier: GPL-3.0-or-later
namespace Gradiwin.Core.Annotations;

/// <summary>Point in "image coordinates": pixels of the processed preview, origin at its center.</summary>
public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public PointD Round() => new(Math.Round(X), Math.Round(Y));
}

public readonly record struct RectD(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public bool Contains(PointD p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    public RectD Inflate(double d) => new(MinX - d, MinY - d, MaxX + d, MaxY + d);
}

/// <summary>Port of drawing_actions.QuadBounds (possibly rotated rectangle).</summary>
public readonly record struct QuadBounds(PointD P1, PointD P2, PointD P3, PointD P4)
{
    public static QuadBounds FromRect(double minX, double minY, double maxX, double maxY) =>
        new(new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY));

    public static QuadBounds FromStartEnd(PointD a, PointD b) =>
        FromRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public PointD[] Points => [P1, P2, P3, P4];

    public RectD BoundingRect
    {
        get
        {
            var pts = Points;
            return new RectD(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        }
    }
}

/// <summary>Text measurement is provided by the renderer (DirectWrite); a crude fallback is used in tests.</summary>
public interface ITextMeasurer
{
    /// <summary>Returns the logical size in pixels of centered multi-line text, and the size of every line.</summary>
    (double Width, double Height, IReadOnlyList<(double Width, double Height)> Lines) Measure(string text, string font, double fontSizePx, bool bold = false);
}

internal sealed class FallbackTextMeasurer : ITextMeasurer
{
    public (double Width, double Height, IReadOnlyList<(double Width, double Height)> Lines) Measure(string text, string font, double fontSizePx, bool bold = false)
    {
        var lines = text.Split('\n').Select(l => (l.Length * fontSizePx * 0.55, fontSizePx * 1.25)).ToList();
        return (lines.Max(l => l.Item1), lines.Sum(l => l.Item2), lines);
    }
}

public enum ResizeHandle
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>Base of every annotation (port of drawing_actions.DrawingAction).</summary>
public abstract class Annotation
{
    private static long s_creationCounter;

    protected Annotation(ToolOptions options)
    {
        Options = options;
        CreationOrder = Interlocked.Increment(ref s_creationCounter);
    }

    public ToolOptions Options { get; protected set; }

    public long CreationOrder { get; protected set; }

    public DrawingMode Mode => Options.Mode;

    public abstract QuadBounds GetBounds();

    public virtual bool ContainsPoint(PointD p) => GetBounds().BoundingRect.Contains(p);

    public abstract void Translate(double dx, double dy);

    /// <summary>Deep copy (used for undoable move/resize/edit).</summary>
    public Annotation Clone()
    {
        var copy = (Annotation)MemberwiseClone();
        copy.Options = Options.Clone();
        copy.CloneMembers();
        return copy;
    }

    protected virtual void CloneMembers() { }

    public virtual bool CanResize => false;

    /// <summary>Port of DrawingAction._calculate_shadow_color.</summary>
    public static (double R, double G, double B, double A) ShadowColorFor(Models.RgbaColor c) =>
        c.Luminance < 0.5 ? (1, 1, 1, 0.05) : (0, 0, 0, 0.3);
}

/// <summary>Port of StrokeAction (pen).</summary>
public class StrokeAnnotation : Annotation
{
    public StrokeAnnotation(IEnumerable<PointD> points, ToolOptions options) : base(options) =>
        Points = points.ToList();

    public List<PointD> Points { get; private set; }

    protected override void CloneMembers() => Points = [.. Points];

    public override QuadBounds GetBounds()
    {
        if (Points.Count == 0)
            return QuadBounds.FromRect(0, 0, 0, 0);
        double pad = Options.Size / 2;
        return QuadBounds.FromRect(Points.Min(p => p.X) - pad, Points.Min(p => p.Y) - pad,
            Points.Max(p => p.X) + pad, Points.Max(p => p.Y) + pad);
    }

    public override void Translate(double dx, double dy) =>
        Points = Points.Select(p => new PointD(p.X + dx, p.Y + dy)).ToList();
}

/// <summary>Port of HighlighterAction (multiply blended, butt caps, width = size*2).</summary>
public sealed class HighlighterAnnotation : StrokeAnnotation
{
    public HighlighterAnnotation(IReadOnlyList<PointD> points, ToolOptions options, bool shift)
        : base(shift && points.Count >= 2 ? [points[0], new PointD(points[^1].X, points[0].Y)] : points, options)
    {
    }

    public override QuadBounds GetBounds()
    {
        if (Points.Count == 0)
            return QuadBounds.FromRect(0, 0, 0, 0);
        double pad = Options.Size + 3;
        return QuadBounds.FromRect(Points.Min(p => p.X) - pad, Points.Min(p => p.Y) - pad,
            Points.Max(p => p.X) + pad, Points.Max(p => p.Y) + pad);
    }
}

/// <summary>Shared logic of LineAction/ArrowAction.</summary>
public abstract class SegmentAnnotation : Annotation
{
    public const double WidthMultiplier = 1.75;

    protected SegmentAnnotation(PointD start, PointD end, bool shift, ToolOptions options) : base(options)
    {
        Start = start;
        if (shift)
        {
            double dx = Math.Abs(end.X - start.X), dy = Math.Abs(end.Y - start.Y);
            End = dx > dy ? new PointD(end.X, start.Y) : new PointD(start.X, end.Y);
        }
        else
        {
            End = end;
        }
    }

    public PointD Start { get; set; }
    public PointD End { get; set; }

    public double StrokeWidth => Options.Size * WidthMultiplier;

    public override bool CanResize => true;

    public override bool ContainsPoint(PointD p)
    {
        double x1 = Start.X, y1 = Start.Y, x2 = End.X, y2 = End.Y;
        double tolerance = 5 + Options.Size * WidthMultiplier;
        double lenSq = (x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1);
        if (lenSq == 0)
            return Math.Sqrt((p.X - x1) * (p.X - x1) + (p.Y - y1) * (p.Y - y1)) < tolerance;
        double t = Math.Clamp(((p.X - x1) * (x2 - x1) + (p.Y - y1) * (y2 - y1)) / lenSq, 0, 1);
        double cx = x1 + t * (x2 - x1), cy = y1 + t * (y2 - y1);
        return (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy) < tolerance * tolerance;
    }

    public override void Translate(double dx, double dy)
    {
        Start = new PointD(Start.X + dx, Start.Y + dy);
        End = new PointD(End.X + dx, End.Y + dy);
    }

    protected QuadBounds PerpendicularQuad(double extent)
    {
        double angle = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
        double pc = -Math.Sin(angle), ps = Math.Cos(angle);
        return new QuadBounds(
            new(Start.X + extent * pc, Start.Y + extent * ps),
            new(End.X + extent * pc, End.Y + extent * ps),
            new(End.X - extent * pc, End.Y - extent * ps),
            new(Start.X - extent * pc, Start.Y - extent * ps));
    }
}

public sealed class LineAnnotation : SegmentAnnotation
{
    public LineAnnotation(PointD start, PointD end, bool shift, ToolOptions options) : base(start, end, shift, options) { }

    public override QuadBounds GetBounds() => PerpendicularQuad(StrokeWidth / 2);
}

public sealed class ArrowAnnotation : SegmentAnnotation
{
    public const double HeadSizeMultiplier = 3;
    public const double MinDistance = 2;
    public const double HeadAngle = Math.PI / 6;
    public const double MinArrowScale = 0.3;
    public const double ScaleDistanceThreshold = 120;

    public ArrowAnnotation(PointD start, PointD end, bool shift, ToolOptions options) : base(start, end, shift, options) { }

    public double HeadSize => Options.Size * HeadSizeMultiplier * WidthMultiplier;

    /// <summary>Arrow head scale for a given on-screen distance (distance and threshold in the same unit).</summary>
    public static double HeadScale(double distance, double threshold) =>
        Math.Min(1.0, Math.Max(MinArrowScale, distance / threshold));

    public override QuadBounds GetBounds()
    {
        double distance = Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2));
        if (distance < MinDistance)
            return QuadBounds.FromRect(Start.X, Start.Y, Start.X, Start.Y);
        return PerpendicularQuad(HeadSize * HeadScale(distance, ScaleDistanceThreshold));
    }
}

/// <summary>Port of RectAction (also the base for ellipse and censor).</summary>
public class RectAnnotation : Annotation
{
    public RectAnnotation(PointD start, PointD end, bool shift, ToolOptions options) : base(options)
    {
        Start = start;
        End = end;
        Shift = shift;
    }

    public PointD Start { get; set; }
    public PointD End { get; set; }
    public bool Shift { get; set; }

    public override bool CanResize => true;

    /// <summary>The end point after applying the shift (square/circle) constraint.</summary>
    public PointD EffectiveEnd
    {
        get
        {
            if (!Shift) return End;
            double size = Math.Max(Math.Abs(End.X - Start.X), Math.Abs(End.Y - Start.Y));
            return new PointD(End.X < Start.X ? Start.X - size : Start.X + size,
                End.Y < Start.Y ? Start.Y - size : Start.Y + size);
        }
    }

    public override QuadBounds GetBounds() => QuadBounds.FromStartEnd(Start, EffectiveEnd);

    public override void Translate(double dx, double dy)
    {
        Start = new PointD(Start.X + dx, Start.Y + dy);
        End = new PointD(End.X + dx, End.Y + dy);
    }
}

public sealed class EllipseAnnotation : RectAnnotation
{
    public EllipseAnnotation(PointD start, PointD end, bool shift, ToolOptions options) : base(start, end, shift, options) { }
}

/// <summary>Port of CensorAction: pixelates whatever is underneath (8 px blocks at creation scale).</summary>
public sealed class CensorAnnotation : RectAnnotation
{
    public const double BaseBlockSize = 8;

    public CensorAnnotation(PointD start, PointD end, ToolOptions options) : base(start, end, false, options) { }

    /// <summary>
    /// Block size in image pixels. Gradia keeps blocks at 8 on-screen pixels at creation time
    /// (block = 8 * scale / original_scale), i.e. constant in image space: 8 / creationScale.
    /// </summary>
    public double BlockSizeImage { get; set; } = BaseBlockSize;

    public override QuadBounds GetBounds() => QuadBounds.FromStartEnd(Start, End);
}

/// <summary>Port of TextAction. Position is the bottom-center anchor.</summary>
public sealed class TextAnnotation : Annotation
{
    public const double PaddingX = 4;
    public const double PaddingY = 2;
    public const double DefaultFontSize = 22;

    public static ITextMeasurer Measurer { get; set; } = new FallbackTextMeasurer();

    public TextAnnotation(PointD position, string text, ToolOptions options, double fontSize) : base(options)
    {
        Position = position;
        Text = text;
        FontSize = fontSize;
    }

    public PointD Position { get; set; }
    public string Text { get; set; }

    /// <summary>Font size in points (Pango semantics; 1pt = 4/3 px).</summary>
    public double FontSize { get; set; }

    public static double PointsToPixels(double pt) => pt * 96.0 / 72.0;

    public bool HasBackground => Options.FillColor.HasAnyComponent && Options.FillColor.A > 0;
    public bool HasOutline => Options.BorderColor.HasAnyComponent && Options.BorderColor.A > 0;

    /// <summary>Port of TextAction.contains_emoji.</summary>
    public bool ContainsEmoji()
    {
        foreach (var rune in Text.EnumerateRunes())
        {
            var cat = System.Text.Rune.GetUnicodeCategory(rune);
            if (cat is System.Globalization.UnicodeCategory.MathSymbol or System.Globalization.UnicodeCategory.CurrencySymbol
                or System.Globalization.UnicodeCategory.ModifierSymbol or System.Globalization.UnicodeCategory.OtherSymbol)
                return true;
            if (rune.Value > 0xFFFF)
                return true;
        }
        return false;
    }

    public override QuadBounds GetBounds()
    {
        if (string.IsNullOrWhiteSpace(Text))
            return QuadBounds.FromRect(Position.X, Position.Y, Position.X, Position.Y);

        var (w, h, _) = Measurer.Measure(Text, Options.Font, PointsToPixels(FontSize));
        int tw = (int)w, th = (int)h;
        double outline = HasOutline ? (int)(2.0 * (FontSize / 14.0)) + 1 : 0;
        return QuadBounds.FromRect(
            Position.X - tw / 2 - PaddingX - outline,
            Position.Y - th - PaddingY - outline,
            Position.X + tw / 2 + PaddingX + outline,
            Position.Y + PaddingY + outline);
    }

    public override void Translate(double dx, double dy) => Position = new PointD(Position.X + dx, Position.Y + dy);
}

/// <summary>Port of NumberStampAction.</summary>
public sealed class NumberAnnotation : Annotation
{
    public NumberAnnotation(PointD position, int number, ToolOptions options) : base(options)
    {
        Position = position;
        Number = number;
    }

    public PointD Position { get; set; }
    public int Number { get; set; }

    public double Radius => Options.Size * 2;

    public override bool ContainsPoint(PointD p)
    {
        double dsq = (p.X - Position.X) * (p.X - Position.X) + (p.Y - Position.Y) * (p.Y - Position.Y);
        return dsq <= (Radius + 5) * (Radius + 5);
    }

    public override QuadBounds GetBounds()
    {
        double outline = Options.BorderColor.HasAnyComponent ? 2 : 0;
        double r = Radius + outline + 1;
        return QuadBounds.FromRect(Position.X - r, Position.Y - r, Position.X + r, Position.Y + r);
    }

    public override void Translate(double dx, double dy) => Position = new PointD(Position.X + dx, Position.Y + dy);
}
