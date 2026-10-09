// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Presets;
using Gradiwin.Rendering;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace Gradiwin.Controls;

/// <summary>
/// The image area: shows the processed preview on a checkerboard, annotation tools
/// (drawing_overlay.py) and the crop overlay (crop_overlay.py).
/// <para>
/// Zoom and pan are handled by a zoomable <see cref="ScrollViewer"/>, i.e. Windows' DirectManipulation:
/// precision-touchpad pinch and two-finger pan, inertia, edge bounce and Ctrl+wheel zoom run on the
/// compositor thread and track the fingers 1:1. The content is a virtualized Win2D surface drawn in fixed
/// "content" coordinates; when a zoom gesture settles it is re-rendered at the new resolution so it stays sharp.
/// Elements that must keep a constant on-screen size (handles, outlines) are scaled by 1/zoom.
/// </para>
/// </summary>
public sealed partial class EditorCanvas : UserControl
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 5.0;
    /// <summary>Space around the image, in content units at 100 % zoom.</summary>
    private const double Pad = 16;
    private const float HandleSize = 8;
    private const double KeyboardPanStep = 50;

    private readonly ScrollViewer _scroll;
    private readonly CanvasVirtualControl _content;
    private CanvasImageBrush? _checker;
    private ElementTheme _checkerTheme;

    private CanvasBitmap? _processed;
    private double _fit = 1;

    // Drawing state
    private bool _isDrawing;
    private readonly List<PointD> _stroke = [];
    private PointD? _start, _end;
    private bool _shift;

    // Selection state
    private Annotation? _selected;
    private bool _moving, _resizing;
    private ResizeHandle _handle;
    private RectD _resizeStartBounds;
    private PointD _resizeStartMouse, _moveStart;
    private Annotation? _beforeEdit;

    // Panning with middle mouse / Space+drag (in viewport coordinates, which don't move while panning)
    private bool _panning;
    private Point _panStartPoint;
    private double _panStartH, _panStartV;

    private double _scrollAccum;
    private bool _cropDragging;

    public EditorCanvas()
    {
        IsTabStop = true;
        _content = new CanvasVirtualControl
        {
            ClearColor = Microsoft.UI.Colors.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _content.RegionsInvalidated += OnRegionsInvalidated;
        _content.CreateResources += (_, _) => _checker = null;

        _scroll = new ScrollViewer
        {
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = (float)MinZoom,
            MaxZoomFactor = (float)MaxZoom,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsHorizontalRailEnabled = false,
            IsVerticalRailEnabled = false,
            IsTabStop = false,
            Content = _content,
        };
        _scroll.ViewChanging += (_, e) => ZoomChanged?.Invoke(e.NextView.ZoomFactor);
        _scroll.ViewChanged += OnViewChanged;
        _scroll.SizeChanged += (_, _) => UpdateLayoutSize();
        Content = _scroll;

        _content.PointerPressed += OnPointerPressed;
        _content.PointerMoved += OnPointerMoved;
        _content.PointerReleased += OnPointerReleased;
        _content.PointerCanceled += (_, e) => OnPointerReleased(this, e);
        _content.PointerWheelChanged += OnPointerWheelChanged;
        _content.DoubleTapped += OnDoubleTapped;
        ActualThemeChanged += (_, _) => Invalidate();
        Unloaded += (_, _) => _content.RemoveFromVisualTree();
    }

    // ---------------- Public state ----------------

    public AnnotationDocument Document { get; } = new();

    /// <summary>Returns the live (persisted) options of a tool.</summary>
    public Func<DrawingMode, ToolOptions> ToolOptionsProvider { get; set; } = ToolOptions.CreateDefault;

    public DrawingMode Mode { get; private set; } = DrawingMode.Select;

    /// <summary>Current text font size in points (TextEntryPopover default 22).</summary>
    public double TextFontSize { get; set; } = TextAnnotation.DefaultFontSize;

    public CropModel Crop { get; } = new();

    public bool CropInteractive { get; private set; }

    public bool AnnotationsVisible { get; set; } = true;

    public Annotation? Selected
    {
        get => _selected;
        private set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            SelectionChanged?.Invoke(value);
        }
    }

    /// <summary>Zoom relative to "fit to window" (1 = fit).</summary>
    public double Zoom => _scroll.ZoomFactor;

    public int PreviewWidth => _processed is null ? 0 : (int)_processed.SizeInPixels.Width;
    public int PreviewHeight => _processed is null ? 0 : (int)_processed.SizeInPixels.Height;

    // Live text being typed (TextEntryPopover preview).
    public TextAnnotation? LiveText { get; private set; }
    public TextAnnotation? EditingText { get; private set; }

    public event Action<Annotation?>? SelectionChanged;
    public event Action<double>? ZoomChanged;
    public event Action<int>? ToolSizeAdjustRequested;
    public event Action<PointD, Point, TextAnnotation?>? TextEntryRequested;
    public event Action? CropChanged;

    public void Invalidate() => _content.Invalidate();

    public void SetProcessed(CanvasBitmap? processed)
    {
        _processed = processed;
        UpdateLayoutSize();
        Invalidate();
    }

    public void SetMode(DrawingMode mode)
    {
        if (mode != DrawingMode.Select)
            Selected = null;
        Mode = mode;
        _isDrawing = _moving = _resizing = false;
        _stroke.Clear();
        _start = _end = null;
        UpdateCursor(null);
        Invalidate();
    }

    public void ClearSelection()
    {
        Selected = null;
        Invalidate();
    }

    public bool DeleteSelected()
    {
        if (_selected is null) return false;
        Document.Remove(_selected);
        Selected = null;
        Invalidate();
        return true;
    }

    public void SetCropInteractive(bool interactive)
    {
        CropInteractive = interactive;
        if (interactive)
            Selected = null;
        UpdateCursor(null);
        Invalidate();
    }

    // ---------------- Text editing (TextEntryPopover) ----------------

    public void BeginTextPreview(PointD position, TextAnnotation? editing, double fontSize)
    {
        EditingText = editing;
        var options = editing?.Options ?? ToolOptionsProvider(DrawingMode.Text).Clone();
        LiveText = new TextAnnotation(editing?.Position ?? position, editing?.Text ?? "", options, fontSize);
        Invalidate();
    }

    public void UpdateTextPreview(string text, double fontSize)
    {
        if (LiveText is null) return;
        LiveText.Text = text;
        LiveText.FontSize = fontSize;
        Invalidate();
    }

    /// <summary>Commits the text entry (port of _on_text_entry_popover_closed).</summary>
    public void EndTextPreview(bool commit)
    {
        var live = LiveText;
        var editing = EditingText;
        LiveText = null;
        EditingText = null;
        if (commit && live is not null)
        {
            var text = live.Text.Trim();
            if (editing is not null)
            {
                if (text.Length > 0)
                {
                    var before = editing.Clone();
                    editing.Text = text;
                    editing.FontSize = live.FontSize;
                    Document.CommitModification(before, editing);
                }
                else
                {
                    Document.Remove(editing);
                    if (ReferenceEquals(Selected, editing)) Selected = null;
                }
            }
            else if (text.Length > 0)
            {
                Document.Add(new TextAnnotation(live.Position, text, ToolOptionsProvider(DrawingMode.Text).Clone(), live.FontSize));
            }
        }
        Invalidate();
    }

    // ---------------- Geometry ----------------
    // Content coordinates: DIPs of the image laid out at "fit" size, offset by Pad. The ScrollViewer
    // scales this whole space by the zoom factor, so pointer positions read relative to _content are
    // already in content coordinates at any zoom.

    /// <summary>On-screen DIPs per content unit (the ScrollViewer zoom).</summary>
    private double ZoomFactor => _scroll.ZoomFactor;

    /// <summary>On-screen DIPs per preview pixel.</summary>
    public double DisplayScale => _fit * ZoomFactor;

    /// <summary>The image in content coordinates (aspect ratio is what callers rely on).</summary>
    public Rect ImageRect => _processed is null
        ? new Rect()
        : new Rect(Pad, Pad, PreviewWidth * _fit, PreviewHeight * _fit);

    private AnnotationTransform Transform =>
        new(new Vector2((float)Pad, (float)Pad), PreviewWidth, PreviewHeight, (float)_fit);

    private PointD ToImage(Point p) => Transform.Unmap(new Vector2((float)p.X, (float)p.Y)).Round();

    private Vector2 ToContent(PointD p) => Transform.Map(p);

    private bool IsInImage(Point p) => ImageRect.Contains(p);

    /// <summary>Recomputes the "fit" scale and the content size from the viewport.</summary>
    private void UpdateLayoutSize()
    {
        if (_processed is null || _scroll.ActualWidth <= 0 || _scroll.ActualHeight <= 0)
            return;
        double vw = Math.Max(1, _scroll.ActualWidth - 2 * Pad), vh = Math.Max(1, _scroll.ActualHeight - 2 * Pad);
        _fit = Math.Max(0.0001, Math.Min(vw / PreviewWidth, vh / PreviewHeight));
        // Floor so the content never exceeds the viewport at 100 % (which would show scrollbars).
        _content.Width = Math.Floor(PreviewWidth * _fit + 2 * Pad);
        _content.Height = Math.Floor(PreviewHeight * _fit + 2 * Pad);
        Invalidate();
    }

    // ---------------- Zoom & pan ----------------

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        ZoomChanged?.Invoke(ZoomFactor);
        if (e.IsIntermediate) return;
        // Re-render at the settled zoom so the image and strokes stay sharp (the compositor scaled the old
        // surface during the gesture). Virtualization keeps this to the visible tiles.
        float scale = (float)Math.Clamp(ZoomFactor, MinZoom, MaxZoom);
        if (Math.Abs(_content.DpiScale - scale) > 0.01f)
            _content.DpiScale = scale;
        else
            Invalidate();
    }

    /// <summary>Animated zoom that keeps <paramref name="anchor"/> (viewport coordinates) fixed.</summary>
    private void ZoomTo(double zoom, Point? anchor = null, bool animate = true)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        double z = ZoomFactor;
        var a = anchor ?? new Point(_scroll.ViewportWidth / 2, _scroll.ViewportHeight / 2);
        double h = (_scroll.HorizontalOffset + a.X) * zoom / z - a.X;
        double v = (_scroll.VerticalOffset + a.Y) * zoom / z - a.Y;
        _scroll.ChangeView(Math.Max(0, h), Math.Max(0, v), (float)zoom, !animate);
    }

    public void SetZoom(double zoom, Point? anchor = null) => ZoomTo(zoom, anchor);

    public void ZoomIn() => ZoomTo(ZoomFactor < 1 && ZoomFactor * 1.25 > 1 ? 1 : ZoomFactor * 1.25);

    public void ZoomOut() => ZoomTo(ZoomFactor > 1 && ZoomFactor / 1.25 < 1 ? 1 : ZoomFactor / 1.25);

    public void ResetZoom() => _scroll.ChangeView(0, 0, 1f, false);

    public void PanBy(double dx, double dy) =>
        _scroll.ChangeView(_scroll.HorizontalOffset - dx, _scroll.VerticalOffset - dy, null, false);

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_content);
        int delta = point.Properties.MouseWheelDelta;
        bool horizontal = point.Properties.IsHorizontalMouseWheel;
        var mods = e.KeyModifiers;
        bool ctrl = mods.HasFlag(VirtualKeyModifiers.Control);
        bool shift = mods.HasFlag(VirtualKeyModifiers.Shift);

        // Gradia: the mouse wheel changes the tool size when the image isn't zoomed (or with Ctrl+Shift).
        // Everything else (Ctrl+wheel zoom, scrolling, touchpad gestures) is left to the ScrollViewer.
        bool adjustSize = (ctrl && shift) || (!ctrl && Math.Abs(ZoomFactor - 1) < 0.01);
        if (!adjustSize || horizontal || !ToolDefinition.For(EffectiveSizeMode).HasScale)
            return;

        e.Handled = true;
        _scrollAccum += delta > 0 ? 0.35 * Math.Abs(delta) / 120.0 : -0.35 * Math.Abs(delta) / 120.0;
        int step = (int)_scrollAccum;
        if (step != 0)
        {
            _scrollAccum -= step;
            ToolSizeAdjustRequested?.Invoke(step);
        }
    }

    private DrawingMode EffectiveSizeMode => _selected?.Mode ?? Mode;

    // ---------------- Pointer input ----------------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var pt = e.GetCurrentPoint(_content);
        var p = pt.Position;
        _shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);

        if (pt.Properties.IsMiddleButtonPressed || (pt.Properties.IsLeftButtonPressed && SpaceHeld))
        {
            _panning = true;
            _panStartPoint = e.GetCurrentPoint(_scroll).Position;
            _panStartH = _scroll.HorizontalOffset;
            _panStartV = _scroll.VerticalOffset;
            _content.CapturePointer(e.Pointer);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed || _processed is null || LiveText is not null)
            return;

        // A finger on a touchscreen would otherwise start a DirectManipulation pan; keep it for the tools.
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch && WantsPointer(p))
            _content.CancelDirectManipulations();

        if (CropInteractive)
        {
            var (x, y, rx, ry, rw, rh) = CropSpace(p);
            if (Crop.BeginDrag(x, y, rx, ry, rw, rh))
            {
                _cropDragging = true;
                _content.CapturePointer(e.Pointer);
            }
            return;
        }

        if (!AnnotationsVisible) return;
        var img = ToImage(p);

        switch (Mode)
        {
            case DrawingMode.Text:
                if (IsInImage(p))
                    TextEntryRequested?.Invoke(img, ContentToView(p), null);
                return;

            case DrawingMode.Number:
                if (IsInImage(p))
                {
                    Document.Add(new NumberAnnotation(img, Document.NextNumber, ToolOptionsProvider(DrawingMode.Number).Clone()));
                    Invalidate();
                }
                return;

            case DrawingMode.Select:
                BeginSelectInteraction(p, img);
                if (_moving || _resizing)
                    _content.CapturePointer(e.Pointer);
                Invalidate();
                return;
        }

        if (!IsInImage(p)) return;
        _isDrawing = true;
        _content.CapturePointer(e.Pointer);
        if (Mode is DrawingMode.Pen or DrawingMode.Highlighter)
        {
            _stroke.Clear();
            _stroke.Add(img);
        }
        else
        {
            _start = _end = img;
        }
        Invalidate();
    }

    /// <summary>Whether a touch at <paramref name="p"/> should go to the tools instead of panning.</summary>
    private bool WantsPointer(Point p)
    {
        if (CropInteractive) return true;
        if (!IsInImage(p)) return false;
        if (Mode != DrawingMode.Select) return true;
        var img = ToImage(p);
        return HandleAt(p) != ResizeHandle.None || InSelectionBounds(img) || Document.HitTest(img) is not null;
    }

    private Point ContentToView(Point p) => _content.TransformToVisual(this).TransformPoint(p);

    /// <summary>
    /// The crop model works in on-screen pixels (30 px handles, 8 px edges), so feed it content
    /// coordinates multiplied by the zoom.
    /// </summary>
    private (double X, double Y, double RX, double RY, double RW, double RH) CropSpace(Point p)
    {
        double z = ZoomFactor;
        var r = ImageRect;
        return (p.X * z, p.Y * z, r.X * z, r.Y * z, r.Width * z, r.Height * z);
    }

    private static bool SpaceHeld =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Space).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void BeginSelectInteraction(Point p, PointD img)
    {
        if (_selected is not null)
        {
            var handle = HandleAt(p);
            if (handle != ResizeHandle.None && _selected.CanResize)
            {
                _resizing = true;
                _handle = handle;
                _resizeStartBounds = _selected.GetBounds().BoundingRect;
                _resizeStartMouse = img;
                _beforeEdit = _selected.Clone();
                return;
            }
            if (InSelectionBounds(img))
            {
                _moving = true;
                _moveStart = img;
                _beforeEdit = _selected.Clone();
                return;
            }
        }

        Selected = Document.HitTest(img);
        if (_selected is not null)
        {
            _moving = true;
            _moveStart = img;
            _beforeEdit = _selected.Clone();
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_content).Position;
        _shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);

        if (_panning)
        {
            var v = e.GetCurrentPoint(_scroll).Position;
            _scroll.ChangeView(_panStartH - (v.X - _panStartPoint.X), _panStartV - (v.Y - _panStartPoint.Y), null, true);
            return;
        }

        if (_cropDragging)
        {
            var (x, y, _, _, rw, rh) = CropSpace(p);
            Crop.UpdateDrag(x, y, rw, rh);
            Invalidate();
            return;
        }

        if (_processed is null)
            return;
        var img = ToImage(p);

        if (_resizing && _selected is not null)
        {
            AnnotationDocument.Resize(_selected, _handle, _resizeStartBounds, _resizeStartMouse, img);
            Invalidate();
            return;
        }
        if (_moving && _selected is not null)
        {
            _selected.Translate(img.X - _moveStart.X, img.Y - _moveStart.Y);
            _moveStart = img;
            Invalidate();
            return;
        }
        if (_isDrawing)
        {
            if (Mode is DrawingMode.Pen or DrawingMode.Highlighter)
            {
                if (_stroke.Count == 0 || _stroke[^1] != img)
                    _stroke.Add(img);
            }
            else
            {
                _end = img;
            }
            Invalidate();
            return;
        }
        UpdateCursor(p);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _content.ReleasePointerCapture(e.Pointer);
        if (_panning)
        {
            _panning = false;
            UpdateCursor(e.GetCurrentPoint(_content).Position);
            return;
        }
        if (_cropDragging)
        {
            _cropDragging = false;
            Crop.EndDrag();
            CropChanged?.Invoke();
            return;
        }

        if (_moving || _resizing)
        {
            if (_selected is not null && _beforeEdit is not null && HasMoved(_beforeEdit, _selected))
                Document.CommitModification(_beforeEdit, _selected);
            _moving = _resizing = false;
            _beforeEdit = null;
            Invalidate();
            return;
        }

        if (!_isDrawing) return;
        _isDrawing = false;
        var created = BuildInProgress(final: true);
        _stroke.Clear();
        _start = _end = null;
        if (created is not null)
            Document.Add(created);
        Invalidate();
    }

    private static bool HasMoved(Annotation before, Annotation after) => before.GetBounds() != after.GetBounds();

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Mode != DrawingMode.Select || _processed is null) return;
        var p = e.GetPosition(_content);
        var img = ToImage(p);
        if (_selected is TextAnnotation text && text.ContainsPoint(img))
            TextEntryRequested?.Invoke(text.Position, ContentToView(p), text);
    }

    /// <summary>Builds the annotation for the current drag (preview or final).</summary>
    private Annotation? BuildInProgress(bool final)
    {
        var options = ToolOptionsProvider(Mode).Clone();
        switch (Mode)
        {
            case DrawingMode.Pen when _stroke.Count > 1:
                return new StrokeAnnotation(_stroke, options);
            case DrawingMode.Highlighter when _stroke.Count > 1:
                return new HighlighterAnnotation(_stroke, options, _shift);
        }
        if (_start is not { } s || _end is not { } en)
            return null;
        if (final && s == en)
            return null;
        return Mode switch
        {
            DrawingMode.Arrow => new ArrowAnnotation(s, en, _shift, options),
            DrawingMode.Line => new LineAnnotation(s, en, _shift, options),
            DrawingMode.Square => new RectAnnotation(s, en, _shift, options),
            DrawingMode.Circle => new EllipseAnnotation(s, en, _shift, options),
            DrawingMode.Censor => new CensorAnnotation(s, en, options) { BlockSizeImage = CensorAnnotation.BaseBlockSize / DisplayScale },
            _ => null,
        };
    }

    // ---------------- Selection helpers ----------------

    private bool InSelectionBounds(PointD img)
    {
        if (_selected is null) return false;
        var b = _selected.GetBounds().BoundingRect;
        double pad = Math.Max(ToolOptionsProvider(Mode).Size, TextFontSize / 2);
        return b.Inflate(pad).Contains(img);
    }

    /// <summary>Handle size in content units, so handles keep 8 px on screen at any zoom.</summary>
    private float HandleContentSize => (float)(HandleSize / ZoomFactor);

    private List<(ResizeHandle Handle, Vector2 TopLeft)> Handles(Annotation a)
    {
        var list = new List<(ResizeHandle, Vector2)>();
        if (!a.CanResize) return list;
        var half = new Vector2(HandleContentSize / 2);
        if (a is SegmentAnnotation seg)
        {
            list.Add((ResizeHandle.TopLeft, ToContent(seg.Start) - half));
            list.Add((ResizeHandle.BottomRight, ToContent(seg.End) - half));
            return list;
        }
        var b = a.GetBounds().BoundingRect;
        var p1 = ToContent(new PointD(b.MinX, b.MinY));
        var p2 = ToContent(new PointD(b.MaxX, b.MaxY));
        list.Add((ResizeHandle.TopLeft, new Vector2(p1.X, p1.Y) - half));
        list.Add((ResizeHandle.TopRight, new Vector2(p2.X, p1.Y) - half));
        list.Add((ResizeHandle.BottomLeft, new Vector2(p1.X, p2.Y) - half));
        list.Add((ResizeHandle.BottomRight, new Vector2(p2.X, p2.Y) - half));
        return list;
    }

    private ResizeHandle HandleAt(Point p)
    {
        if (_selected is null || !_selected.CanResize) return ResizeHandle.None;
        float size = HandleContentSize;
        foreach (var (handle, tl) in Handles(_selected))
        {
            if (p.X >= tl.X && p.X <= tl.X + size && p.Y >= tl.Y && p.Y <= tl.Y + size)
                return handle;
        }
        if (_selected is SegmentAnnotation) return ResizeHandle.None;

        var b = _selected.GetBounds().BoundingRect;
        var v1 = ToContent(new PointD(b.MinX, b.MinY));
        var v2 = ToContent(new PointD(b.MaxX, b.MaxY));
        double m = size;
        if (Math.Abs(p.Y - v1.Y) <= m && p.X >= v1.X && p.X <= v2.X) return ResizeHandle.Top;
        if (Math.Abs(p.Y - v2.Y) <= m && p.X >= v1.X && p.X <= v2.X) return ResizeHandle.Bottom;
        if (Math.Abs(p.X - v1.X) <= m && p.Y >= v1.Y && p.Y <= v2.Y) return ResizeHandle.Left;
        if (Math.Abs(p.X - v2.X) <= m && p.Y >= v1.Y && p.Y <= v2.Y) return ResizeHandle.Right;
        return ResizeHandle.None;
    }

    private void UpdateCursor(Point? p)
    {
        InputSystemCursorShape shape = InputSystemCursorShape.Arrow;
        if (_processed is not null && p is { } pt)
        {
            if (CropInteractive)
            {
                var (x, y, rx, ry, rw, rh) = CropSpace(pt);
                shape = Crop.HitTest(x, y, rx, ry, rw, rh) switch
                {
                    CropHit.TopLeft or CropHit.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
                    CropHit.TopRight or CropHit.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
                    CropHit.Top or CropHit.Bottom => InputSystemCursorShape.SizeNorthSouth,
                    CropHit.Left or CropHit.Right => InputSystemCursorShape.SizeWestEast,
                    CropHit.Area => InputSystemCursorShape.SizeAll,
                    _ => InputSystemCursorShape.Cross,
                };
            }
            else if (Mode == DrawingMode.Select)
            {
                var img = ToImage(pt);
                var handle = HandleAt(pt);
                if (handle != ResizeHandle.None)
                {
                    shape = _selected is SegmentAnnotation ? InputSystemCursorShape.SizeAll : handle switch
                    {
                        ResizeHandle.TopLeft or ResizeHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
                        ResizeHandle.TopRight or ResizeHandle.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
                        ResizeHandle.Top or ResizeHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
                        _ => InputSystemCursorShape.SizeWestEast,
                    };
                }
                else if (_selected is not null && InSelectionBounds(img))
                {
                    shape = InputSystemCursorShape.SizeAll;
                }
                else if (Document.HitTest(img) is not null)
                {
                    shape = InputSystemCursorShape.Hand;
                }
            }
            else if (IsInImage(pt))
            {
                shape = Mode == DrawingMode.Text ? InputSystemCursorShape.IBeam : InputSystemCursorShape.Cross;
            }
        }
        ProtectedCursor = InputSystemCursor.Create(shape);
    }

    // ---------------- Drawing ----------------

    private CanvasImageBrush GetChecker(ICanvasResourceCreator rc)
    {
        if (_checker is not null && _checkerTheme == ActualTheme) return _checker;
        _checker?.Dispose();
        _checkerTheme = ActualTheme;
        bool light = ActualTheme == ElementTheme.Light;
        var c1 = light ? Windows.UI.Color.FromArgb(255, 255, 255, 255) : Windows.UI.Color.FromArgb(255, 0x4a, 0x4a, 0x4a);
        var c2 = light ? Windows.UI.Color.FromArgb(255, 0xd6, 0xd6, 0xd6) : Windows.UI.Color.FromArgb(255, 0x3a, 0x3a, 0x3a);
        var tile = new CanvasRenderTarget(rc, 16, 16, 96);
        using (var ds = tile.CreateDrawingSession())
        {
            ds.Clear(c1);
            ds.FillRectangle(0, 0, 8, 8, c2);
            ds.FillRectangle(8, 8, 8, 8, c2);
        }
        _checker = new CanvasImageBrush(rc, tile)
        {
            ExtendX = CanvasEdgeBehavior.Wrap,
            ExtendY = CanvasEdgeBehavior.Wrap,
            Interpolation = CanvasImageInterpolation.NearestNeighbor,
        };
        return _checker;
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        foreach (var region in args.InvalidatedRegions)
        {
            using var ds = sender.CreateDrawingSession(region);
            DrawScene(sender, ds);
        }
    }

    private void DrawScene(ICanvasResourceCreator rc, CanvasDrawingSession ds)
    {
        if (_processed is null) return;

        var rect = ImageRect;
        var t = Transform;
        float z = (float)ZoomFactor;

        // Transparency checkerboard (transparency_overlay.py), kept at a constant on-screen size.
        var checker = GetChecker(rc);
        checker.Transform = Matrix3x2.CreateScale(1 / z) * Matrix3x2.CreateTranslation((float)rect.X, (float)rect.Y);
        ds.FillRectangle(rect, checker);

        var interpolation = DisplayScale >= 2 ? CanvasImageInterpolation.NearestNeighbor : CanvasImageInterpolation.HighQualityCubic;
        ds.DrawImage(_processed, rect, new Rect(0, 0, PreviewWidth, PreviewHeight), 1f, interpolation);

        if (AnnotationsVisible)
        {
            using (ds.CreateLayer(1f, rect))
            {
                foreach (var a in Document.Items)
                {
                    if (ReferenceEquals(a, EditingText)) continue;
                    AnnotationRenderer.Draw(ds, a, t, _processed, 1f);
                }
                if (_isDrawing && BuildInProgress(final: false) is { } preview)
                    AnnotationRenderer.Draw(ds, preview, t, _processed, 1f);
                if (LiveText is { Text.Length: > 0 } live)
                    AnnotationRenderer.Draw(ds, live, t, _processed, 1f);
            }
            if (_selected is not null && !CropInteractive)
                DrawSelection(ds, _selected, z);
        }

        if (CropInteractive || Crop.HasCrop)
            DrawCrop(ds, rect, z);
    }

    private void DrawSelection(CanvasDrawingSession ds, Annotation a, float z)
    {
        var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
        var pts = a.GetBounds().Points.Select(p => ToContent(new PointD((int)p.X, (int)p.Y))).ToArray();
        using (var geo = CanvasGeometry.CreatePolygon(ds, pts))
            ds.DrawGeometry(geo, accent, 2 / z);
        float size = HandleContentSize;
        foreach (var (_, tl) in Handles(a))
        {
            ds.FillRectangle(tl.X, tl.Y, size, size, Microsoft.UI.Colors.White);
            ds.DrawRectangle(tl.X, tl.Y, size, size, accent, 1.5f / z);
        }
    }

    private void DrawCrop(CanvasDrawingSession ds, Rect img, float z)
    {
        double cx = img.X + Crop.X * img.Width, cy = img.Y + Crop.Y * img.Height;
        double cw = Crop.Width * img.Width, ch = Crop.Height * img.Height;
        var dim = Windows.UI.Color.FromArgb((byte)(0.6 * 255), 51, 51, 51);

        using (var outside = CanvasGeometry.CreateRectangle(ds, img))
        using (var inside = CanvasGeometry.CreateRectangle(ds, new Rect(cx, cy, cw, ch)))
        using (var mask = outside.CombineWith(inside, Matrix3x2.Identity, CanvasGeometryCombine.Exclude))
            ds.FillGeometry(mask, dim);

        var border = Windows.UI.Color.FromArgb((byte)((CropInteractive ? 0.8 : 0.5) * 255), 255, 255, 255);
        ds.DrawRectangle((float)cx, (float)cy, (float)cw, (float)ch, border, 2 / z);

        if (!CropInteractive) return;
        float lw = 4 / z, len = 25 / z;
        var white = Microsoft.UI.Colors.White;
        float x = (float)cx, y = (float)cy, w = (float)cw, h = (float)ch;
        // Corner brackets like _draw_corner_lines.
        ds.FillRectangle(x - lw, y - lw, len + lw, lw, white);
        ds.FillRectangle(x - lw, y - lw, lw, len + lw, white);
        ds.FillRectangle(x + w - len, y - lw, len + lw, lw, white);
        ds.FillRectangle(x + w, y - lw, lw, len + lw, white);
        ds.FillRectangle(x - lw, y + h, len + lw, lw, white);
        ds.FillRectangle(x - lw, y + h - len, lw, len + lw, white);
        ds.FillRectangle(x + w - len, y + h, len + lw, lw, white);
        ds.FillRectangle(x + w, y + h - len, lw, len + lw, white);
    }
}
