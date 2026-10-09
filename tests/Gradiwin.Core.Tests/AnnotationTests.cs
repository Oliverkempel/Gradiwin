using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Tests;

public class AnnotationTests
{
    private static ToolOptions Opt(DrawingMode m) => ToolOptions.CreateDefault(m);

    [Fact]
    public void NumbersFollowCreationOrderAfterUndoRedoAndDelete()
    {
        var doc = new AnnotationDocument();
        var a = new NumberAnnotation(new(0, 0), doc.NextNumber, Opt(DrawingMode.Number));
        doc.Add(a);
        var b = new NumberAnnotation(new(50, 0), doc.NextNumber, Opt(DrawingMode.Number));
        doc.Add(b);
        var c = new NumberAnnotation(new(100, 0), doc.NextNumber, Opt(DrawingMode.Number));
        doc.Add(c);
        Assert.Equal([1, 2, 3], new[] { a.Number, b.Number, c.Number });

        doc.Remove(a);
        Assert.Equal([1, 2], new[] { b.Number, c.Number });

        doc.Undo();
        Assert.Equal([1, 2, 3], new[] { a.Number, b.Number, c.Number });
        Assert.True(doc.CanRedo);

        doc.Redo();
        Assert.Equal(2, doc.Items.Count);
    }

    [Fact]
    public void NewActionClearsRedoStack()
    {
        var doc = new AnnotationDocument();
        doc.Add(new StrokeAnnotation([new(0, 0), new(1, 1)], Opt(DrawingMode.Pen)));
        doc.Undo();
        Assert.True(doc.CanRedo);
        doc.Add(new StrokeAnnotation([new(0, 0), new(2, 2)], Opt(DrawingMode.Pen)));
        Assert.False(doc.CanRedo);
    }

    [Fact]
    public void MoveIsUndoable()
    {
        var doc = new AnnotationDocument();
        var rect = new RectAnnotation(new(0, 0), new(10, 10), false, Opt(DrawingMode.Square));
        doc.Add(rect);
        var before = rect.Clone();
        rect.Translate(5, 5);
        doc.CommitModification(before, rect);
        Assert.Equal(new PointD(5, 5), ((RectAnnotation)doc.Items[0]).Start);
        doc.Undo();
        Assert.Equal(new PointD(0, 0), ((RectAnnotation)doc.Items[0]).Start);
        doc.Redo();
        Assert.Equal(new PointD(5, 5), ((RectAnnotation)doc.Items[0]).Start);
    }

    [Fact]
    public void ClearIsUndoable()
    {
        var doc = new AnnotationDocument();
        doc.Add(new LineAnnotation(new(0, 0), new(10, 0), false, Opt(DrawingMode.Line)));
        doc.Clear();
        Assert.True(doc.IsEmpty);
        doc.Undo();
        Assert.Single(doc.Items);
    }

    [Fact]
    public void LineHitTestUsesDistanceToSegment()
    {
        var line = new LineAnnotation(new(0, 0), new(100, 0), false, Opt(DrawingMode.Line));
        // tolerance = 5 + 14*1.75 = 29.5
        Assert.True(line.ContainsPoint(new(50, 29)));
        Assert.False(line.ContainsPoint(new(50, 31)));
        Assert.False(line.ContainsPoint(new(140, 0)));
    }

    [Fact]
    public void ShiftConstrainsSegmentsAndRects()
    {
        var arrow = new ArrowAnnotation(new(0, 0), new(100, 20), true, Opt(DrawingMode.Arrow));
        Assert.Equal(new PointD(100, 0), arrow.End);

        var square = new RectAnnotation(new(0, 0), new(30, -10), true, Opt(DrawingMode.Square));
        Assert.Equal(new PointD(30, -30), square.EffectiveEnd);

        var hl = new HighlighterAnnotation([new(0, 0), new(10, 5), new(40, 9)], Opt(DrawingMode.Highlighter), true);
        Assert.Equal([new PointD(0, 0), new PointD(40, 0)], hl.Points);
    }

    [Fact]
    public void ResizeRectFromCorner()
    {
        var rect = new RectAnnotation(new(0, 0), new(10, 10), false, Opt(DrawingMode.Square));
        AnnotationDocument.Resize(rect, ResizeHandle.BottomRight, rect.GetBounds().BoundingRect, new(10, 10), new(20, 15));
        Assert.Equal(new PointD(20, 15), rect.End);
        Assert.Equal(new PointD(0, 0), rect.Start);
    }

    [Fact]
    public void ToolOptionsSerializationMatchesGradiaSchema()
    {
        const string gradia = "{\"mode\": \"HIGHLIGHTER\", \"size\": 14, \"primary_color\": [0.96, 0.83, 0.18, 0.4], \"fill_color\": [1.0, 1.0, 1.0, 1.0], \"border_color\": [0.0, 0.0, 0.0, 1.0]}";
        var o = ToolOptions.Deserialize(gradia);
        Assert.Equal(DrawingMode.Highlighter, o.Mode);
        Assert.Equal(14, o.Size);
        Assert.Equal(new RgbaColor(0.96, 0.83, 0.18, 0.4), o.PrimaryColor);
        var again = ToolOptions.Deserialize(o.Serialize());
        Assert.Equal(o.PrimaryColor, again.PrimaryColor);
        Assert.Equal(o.Font, again.Font);
    }

    [Fact]
    public void TextEmojiDetection()
    {
        var o = Opt(DrawingMode.Text);
        Assert.False(new TextAnnotation(new(0, 0), "Hello", o, 22).ContainsEmoji());
        Assert.True(new TextAnnotation(new(0, 0), "Hi 😀", o, 22).ContainsEmoji());
    }
}
