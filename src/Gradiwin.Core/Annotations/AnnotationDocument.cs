// SPDX-License-Identifier: GPL-3.0-or-later
namespace Gradiwin.Core.Annotations;

/// <summary>
/// The list of annotations plus undo/redo (port of DrawingOverlay's actions/redo_stack).
/// Unlike Gradia, moves, resizes and text edits are undoable too.
/// </summary>
public sealed class AnnotationDocument
{
    private abstract record Command;
    private sealed record AddCommand(Annotation Item) : Command;
    private sealed record RemoveCommand(Annotation Item, int Index) : Command;
    private sealed record ReplaceCommand(Annotation Before, Annotation After) : Command;
    private sealed record ClearCommand(List<Annotation> Items) : Command;

    private readonly List<Annotation> _items = [];
    private readonly Stack<Command> _undo = new();
    private readonly Stack<Command> _redo = new();

    public IReadOnlyList<Annotation> Items => _items;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsEmpty => _items.Count == 0;

    public event Action? Changed;

    public int NextNumber => _items.OfType<NumberAnnotation>().Count() + 1;

    public void Add(Annotation item)
    {
        _items.Add(item);
        Push(new AddCommand(item));
    }

    public bool Remove(Annotation item)
    {
        int index = _items.IndexOf(item);
        if (index < 0) return false;
        _items.RemoveAt(index);
        Push(new RemoveCommand(item, index));
        return true;
    }

    /// <summary>Records that <paramref name="current"/> (still in the list) was modified from the <paramref name="before"/> snapshot.</summary>
    public void CommitModification(Annotation before, Annotation current)
    {
        int index = _items.IndexOf(current);
        if (index < 0) return;
        Push(new ReplaceCommand(before, current));
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        Push(new ClearCommand([.. _items]));
        _items.Clear();
    }

    /// <summary>Drops everything without recording history (new image loaded).</summary>
    public void Reset()
    {
        _items.Clear();
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    public Annotation? HitTest(PointD p)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
            if (_items[i].ContainsPoint(p))
                return _items[i];
        return null;
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var cmd = _undo.Pop();
        switch (cmd)
        {
            case AddCommand a:
                _items.Remove(a.Item);
                break;
            case RemoveCommand r:
                _items.Insert(Math.Min(r.Index, _items.Count), r.Item);
                break;
            case ReplaceCommand rep:
                Swap(rep.After, rep.Before);
                break;
            case ClearCommand c:
                _items.AddRange(c.Items);
                break;
        }
        _redo.Push(cmd);
        AfterChange();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var cmd = _redo.Pop();
        switch (cmd)
        {
            case AddCommand a:
                _items.Add(a.Item);
                break;
            case RemoveCommand r:
                _items.Remove(r.Item);
                break;
            case ReplaceCommand rep:
                Swap(rep.Before, rep.After);
                break;
            case ClearCommand:
                _items.Clear();
                break;
        }
        _undo.Push(cmd);
        AfterChange();
    }

    private void Swap(Annotation from, Annotation to)
    {
        int i = _items.IndexOf(from);
        if (i >= 0) _items[i] = to;
    }

    private void Push(Command cmd)
    {
        _undo.Push(cmd);
        _redo.Clear();
        AfterChange();
    }

    private void AfterChange()
    {
        Renumber();
        Changed?.Invoke();
    }

    /// <summary>Port of DrawingOverlay._renumber_actions: numbers follow creation order.</summary>
    public void Renumber()
    {
        int n = 1;
        foreach (var stamp in _items.OfType<NumberAnnotation>().OrderBy(a => a.CreationOrder))
            stamp.Number = n++;
    }

    /// <summary>Port of DrawingOverlay._resize_action. Coordinates are image coordinates.</summary>
    public static void Resize(Annotation action, ResizeHandle handle, RectD startBounds, PointD startMouse, PointD currentMouse)
    {
        switch (action)
        {
            case SegmentAnnotation seg:
                if (handle == ResizeHandle.TopLeft)
                    seg.Start = currentMouse.Round();
                else if (handle == ResizeHandle.BottomRight)
                    seg.End = currentMouse.Round();
                break;

            case RectAnnotation rect:
                double minX = startBounds.MinX, minY = startBounds.MinY, maxX = startBounds.MaxX, maxY = startBounds.MaxY;
                double dx = currentMouse.X - startMouse.X, dy = currentMouse.Y - startMouse.Y;
                if (handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight) minY += dy;
                if (handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight) maxY += dy;
                if (handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft) minX += dx;
                if (handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight) maxX += dx;
                rect.Shift = false;
                rect.Start = new PointD((int)minX, (int)minY);
                rect.End = new PointD((int)maxX, (int)maxY);
                break;
        }
    }
}
