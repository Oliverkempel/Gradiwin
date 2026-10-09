// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Presets;
using Gradiwin.Rendering;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace Gradiwin.Controls;

/// <summary>
/// The image area: shows the processed preview on a checkerboard, handles zoom/pan
/// (zoom_controller.py), annotation tools (drawing_overlay.py) and the crop overlay (crop_overlay.py).
/// </summary>
public sealed partial class EditorCanvas : UserControl
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 5.0;
    private const double ViewMargin = 16;
    private const float HandleSize = 8;
    private const double PanStep = 30;

    private readonly CanvasControl _canvas;
    private CanvasImageBrush? _checker;
    private ElementTheme _checkerTheme;

    private CanvasBitmap? _processed;
    private double _zoom = 1;
    private Vector2 _pan;

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

    // Panning with middle mouse / space
    private bool _panning;
    private Point _panStartPoint;
    private Vector2 _panStartValue;

    private double _scrollAccum;
    private bool _cropDragging;

    public EditorCanvas()
    {
        IsTabStop = true;
        _canvas = new CanvasControl { ClearColor = Microsoft.UI.Colors.Transparent };
        _canvas.Draw += OnDraw;
        _canvas.CreateResources += (_, _) => _checker = null;
        Content = _canvas;

        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.PointerCanceled += (_, e) => OnPointerReleased(this, e);
        _canvas.PointerWheelChanged += OnPointerWheelChanged;
        _canvas.DoubleTapped += OnDoubleTapped;
        SizeChanged += (_, _) => { ClampPan(); Invalidate(); };
        ActualThemeChanged += (_, _) => Invalidate();
        Unloaded += (_, _) => _canvas.RemoveFromVisualTree();
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

    public double Zoom => _zoom;

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

    public void Invalidate() => _canvas.Invalidate();

    public void SetProcessed(CanvasBitmap? processed)
    {
        bool sizeChanged = _processed is null || processed is null ||
                           _processed.SizeInPixels.Width != processed.SizeInPixels.Width ||
                           _processed.SizeInPixels.Height != processed.SizeInPixels.Height;
        _processed = processed;
        if (sizeChanged)
            ClampPan();
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

    private double FitScale
    {
        get
        {
            if (_processed is null) return 1;
            double vw = Math.Max(1, ActualWidth - 2 * ViewMargin), vh = Math.Max(1, ActualHeight - 2 * ViewMargin);
            return Math.Max(0.0001, Math.Min(vw / PreviewWidth, vh / PreviewHeight));
        }
    }

    /// <summary>View DIPs per preview pixel.</summary>
    public double DisplayScale => FitScale * _zoom;

    public Rect ImageRect
    {
        get
        {
            if (_processed is null) return new Rect();
            double s = DisplayScale;
            double dw = PreviewWidth * s, dh = PreviewHeight * s;
            return new Rect((ActualWidth - dw) / 2 + _pan.X, (ActualHeight - dh) / 2 + _pan.Y, dw, dh);
        }
    }

    private AnnotationTransform Transform
    {
        get
        {
            var r = ImageRect;
            return new AnnotationTransform(new Vector2((float)r.X, (float)r.Y), PreviewWidth, PreviewHeight, (float)DisplayScale);
        }
    }

    private PointD ToImage(Point p) => Transform.Unmap(new Vector2((float)p.X, (float)p.Y)).Round();

    private Vector2 ToView(PointD p) => Transform.Map(p);

    private bool IsInImage(Point p) => ImageRect.Contains(p);

    // ---------------- Zoom & pan (zoom_controller.py) ----------------

    public void SetZoom(double zoom, Point? anchor = null)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (_processed is null || Math.Abs(zoom - _zoom) < 1e-9)
        {
            _zoom = zoom;
            ZoomChanged?.Invoke(_zoom);
            return;
        }
        var a = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var rect = ImageRect;
        double s = DisplayScale;
        double ix = (a.X - rect.X) / s, iy = (a.Y - rect.Y) / s;

        _zoom = zoom;
        double ns = DisplayScale;
        double centeredX = (ActualWidth - PreviewWidth * ns) / 2;
        double centeredY = (ActualHeight - PreviewHeight * ns) / 2;
        _pan = new Vector2((float)(a.X - ix * ns - centeredX), (float)(a.Y - iy * ns - centeredY));
        ClampPan();
        Invalidate();
        ZoomChanged?.Invoke(_zoom);
    }

    public void ZoomIn() => SetZoom(_zoom * 1.1 > 1 && _zoom < 1 ? 1 : _zoom * 1.25);
    public void ZoomOut() => SetZoom(_zoom / 1.25 < 1 && _zoom > 1 ? 1 : _zoom / 1.25);

    public void ResetZoom()
    {
        _pan = Vector2.Zero;
        SetZoom(1);
        Invalidate();
    }

    public void PanBy(double dx, double dy)
    {
        _pan += new Vector2((float)dx, (float)dy);
        ClampPan();
        Invalidate();
    }

    private void ClampPan()
    {
        if (_processed is null)
        {
            _pan = Vector2.Zero;
            return;
        }
        double s = DisplayScale;
        double dw = PreviewWidth * s, dh = PreviewHeight * s;
        double maxX = Math.Max(0, (dw - ActualWidth) / 2 + ViewMargin);
        double maxY = Math.Max(0, (dh - ActualHeight) / 2 + ViewMargin);
        _pan = new Vector2((float)Math.Clamp(_pan.X, -maxX, maxX), (float)Math.Clamp(_pan.Y, -maxY, maxY));
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas);
        int delta = point.Properties.MouseWheelDelta;
        bool horizontal = point.Properties.IsHorizontalMouseWheel;
        var mods = e.KeyModifiers;
        bool ctrl = mods.HasFlag(VirtualKeyModifiers.Control);
        bool shift = mods.HasFlag(VirtualKeyModifiers.Shift);
        e.Handled = true;

        if (ctrl && !shift && !horizontal)
        {
            // Precision touchpad pinch also arrives as Ctrl+wheel.
            double factor = Math.Pow(1.1, delta / 120.0);
            SetZoom(_zoom * factor, point.Position);
            return;
        }

        bool adjustSize = (ctrl && shift) || Math.Abs(_zoom - 1) < 1e-9;
        if (adjustSize && !horizontal && ToolDefinition.For(EffectiveSizeMode).HasScale)
        {
            _scrollAccum += delta > 0 ? 0.35 * Math.Abs(delta) / 120.0 : -0.35 * Math.Abs(delta) / 120.0;
            int step = (int)_scrollAccum;
            if (step != 0)
            {
                _scrollAccum -= step;
                ToolSizeAdjustRequested?.Invoke(step);
            }
            return;
        }

        double amount = PanStep * delta / 120.0;
        if (horizontal)
            PanBy(-amount, 0);
        else if (shift)
            PanBy(amount, 0);
        else
            PanBy(0, amount);
    }

    private DrawingMode EffectiveSizeMode => _selected?.Mode ?? Mode;

    // ---------------- Pointer input ----------------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var pt = e.GetCurrentPoint(_canvas);
        var p = pt.Position;
        _shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);

        if (pt.Properties.IsMiddleButtonPressed || (pt.Properties.IsLeftButtonPressed && SpaceHeld))
        {
            _panning = true;
            _panStartPoint = p;
            _panStartValue = _pan;
            _canvas.CapturePointer(e.Pointer);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed || _processed is null || LiveText is not null)
            return;

        if (CropInteractive)
        {
            var r = ImageRect;
            if (Crop.BeginDrag(p.X, p.Y, r.X, r.Y, r.Width, r.Height))
            {
                _cropDragging = true;
                _canvas.CapturePointer(e.Pointer);
            }
            return;
        }

        if (!AnnotationsVisible) return;
        var img = ToImage(p);

        switch (Mode)
        {
            case DrawingMode.Text:
                if (IsInImage(p))
                    TextEntryRequested?.Invoke(img, p, null);
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
                    _canvas.CapturePointer(e.Pointer);
                Invalidate();
                return;
        }

        if (!IsInImage(p)) return;
        _isDrawing = true;
        _canvas.CapturePointer(e.Pointer);
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
        var p = e.GetCurrentPoint(_canvas).Position;
        _shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);

        if (_panning)
        {
            _pan = _panStartValue + new Vector2((float)(p.X - _panStartPoint.X), (float)(p.Y - _panStartPoint.Y));
            ClampPan();
            Invalidate();
            return;
        }

        if (_cropDragging)
        {
            var r = ImageRect;
            Crop.UpdateDrag(p.X, p.Y, r.Width, r.Height);
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
        _canvas.ReleasePointerCapture(e.Pointer);
        if (_panning)
        {
            _panning = false;
            UpdateCursor(e.GetCurrentPoint(_canvas).Position);
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
        var p = e.GetPosition(_canvas);
        var img = ToImage(p);
        if (_selected is TextAnnotation text && text.ContainsPoint(img))
            TextEntryRequested?.Invoke(text.Position, p, text);
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

    private List<(ResizeHandle Handle, Vector2 TopLeft)> Handles(Annotation a)
    {
        var list = new List<(ResizeHandle, Vector2)>();
        if (!a.CanResize) return list;
        var half = new Vector2(HandleSize / 2);
        if (a is SegmentAnnotation seg)
        {
            list.Add((ResizeHandle.TopLeft, ToView(seg.Start) - half));
            list.Add((ResizeHandle.BottomRight, ToView(seg.End) - half));
            return list;
        }
        var b = a.GetBounds().BoundingRect;
        var p1 = ToView(new PointD(b.MinX, b.MinY));
        var p2 = ToView(new PointD(b.MaxX, b.MaxY));
        list.Add((ResizeHandle.TopLeft, new Vector2(p1.X, p1.Y) - half));
        list.Add((ResizeHandle.TopRight, new Vector2(p2.X, p1.Y) - half));
        list.Add((ResizeHandle.BottomLeft, new Vector2(p1.X, p2.Y) - half));
        list.Add((ResizeHandle.BottomRight, new Vector2(p2.X, p2.Y) - half));
        return list;
    }

    private ResizeHandle HandleAt(Point p)
    {
        if (_selected is null || !_selected.CanResize) return ResizeHandle.None;
        foreach (var (handle, tl) in Handles(_selected))
        {
            if (p.X >= tl.X && p.X <= tl.X + HandleSize && p.Y >= tl.Y && p.Y <= tl.Y + HandleSize)
                return handle;
        }
        if (_selected is SegmentAnnotation) return ResizeHandle.None;

        var b = _selected.GetBounds().BoundingRect;
        var v1 = ToView(new PointD(b.MinX, b.MinY));
        var v2 = ToView(new PointD(b.MaxX, b.MaxY));
        double m = HandleSize;
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
                var r = ImageRect;
                shape = Crop.HitTest(pt.X, pt.Y, r.X, r.Y, r.Width, r.Height) switch
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

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        if (_processed is null) return;

        var rect = ImageRect;
        var t = Transform;

        // Transparency checkerboard (transparency_overlay.py).
        var checker = GetChecker(sender);
        checker.Transform = Matrix3x2.CreateTranslation((float)rect.X, (float)rect.Y);
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
                DrawSelection(ds, _selected);
        }

        if (CropInteractive || Crop.HasCrop)
            DrawCrop(ds, rect);
    }

    private void DrawSelection(CanvasDrawingSession ds, Annotation a)
    {
        var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
        var pts = a.GetBounds().Points.Select(p => ToView(new PointD((int)p.X, (int)p.Y))).ToArray();
        using (var geo = CanvasGeometry.CreatePolygon(ds, pts))
            ds.DrawGeometry(geo, accent, 2);
        foreach (var (_, tl) in Handles(a))
        {
            ds.FillRectangle(tl.X, tl.Y, HandleSize, HandleSize, Microsoft.UI.Colors.White);
            ds.DrawRectangle(tl.X, tl.Y, HandleSize, HandleSize, accent, 1.5f);
        }
    }

    private void DrawCrop(CanvasDrawingSession ds, Rect img)
    {
        double cx = img.X + Crop.X * img.Width, cy = img.Y + Crop.Y * img.Height;
        double cw = Crop.Width * img.Width, ch = Crop.Height * img.Height;
        var dim = Windows.UI.Color.FromArgb((byte)(0.6 * 255), 51, 51, 51);

        using (var outside = CanvasGeometry.CreateRectangle(ds, img))
        using (var inside = CanvasGeometry.CreateRectangle(ds, new Rect(cx, cy, cw, ch)))
        using (var mask = outside.CombineWith(inside, Matrix3x2.Identity, CanvasGeometryCombine.Exclude))
            ds.FillGeometry(mask, dim);

        var border = Windows.UI.Color.FromArgb((byte)((CropInteractive ? 0.8 : 0.5) * 255), 255, 255, 255);
        ds.DrawRectangle((float)cx, (float)cy, (float)cw, (float)ch, border, 2);

        if (!CropInteractive) return;
        const float lw = 4, len = 25;
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
