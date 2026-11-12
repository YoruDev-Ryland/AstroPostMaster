using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using System.Globalization;

namespace AstroPostMaster.App.Views;

/// <summary>
/// The image with every slide's frame drawn over it. Drag inside a frame to move it, drag a corner handle to
/// resize (ratio locked unless the post is free, which adds edge handles), drag the knob above the frame to rotate
/// it (Shift snaps to 5°, double-click resets). The selected frame is the only accent-coloured element; everything
/// outside it is dimmed.
/// </summary>
public sealed class CropCanvas : Control, ICustomHitTest
{
    public static readonly StyledProperty<EditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<CropCanvas, EditorViewModel?>(nameof(Editor));

    private const double Inset = 16;
    private const double HandleSize = 9;
    private const double HandleHit = 12;
    private const double RotateOffset = 24;
    private const double RotateKnob = 5;

    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor NwseCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor NeswCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor NsCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor WeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor RotateCursor = new(StandardCursorType.Cross);

    private enum DragMode { None, Move, Resize, Rotate, Pan }

    private readonly Viewport _viewport = new();
    private object? _viewportImage;
    private Point _panLast;

    private DragMode _mode;
    private SlideViewModel? _dragSlide;
    private Point _dragStart;
    private RectF _startRect;
    private Point _anchor; // resize anchor in source pixels
    private (int X, int Y) _handle; // dragged handle, frame-local −1…1
    private Size _startSize; // frame size in source pixels when the resize began

    static CropCanvas()
    {
        AffectsRender<CropCanvas>(EditorProperty);
        FocusableProperty.OverrideDefaultValue<CropCanvas>(true);
    }

    public EditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>Raised when the user clicks the empty canvas (no image yet).</summary>
    public event EventHandler? OpenRequested;

    // Hit-testable across its whole area (it paints a background), but never outside its own bounds:
    // returning true unconditionally made the canvas swallow clicks meant for the toolbar above it.
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    /// <summary>While true (Space held), a left drag pans instead of moving frames.</summary>
    public bool PanModifier { get; set; }

    public double Zoom => _viewport.Zoom;

    public void ResetZoom()
    {
        _viewport.Reset();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != EditorProperty) return;
        if (change.OldValue is EditorViewModel old) old.FramesChanged -= InvalidateVisual;
        if (change.NewValue is EditorViewModel current) current.FramesChanged += InvalidateVisual;
        _viewport.Reset();
        InvalidateVisual();
    }

    // ---------- rendering ----------

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("CanvasBrush", Brushes.Black), bounds);

        var editor = Editor;
        if (editor?.Preview is not Bitmap bitmap)
        {
            var message = editor is null ? "Create a post, or drop an image here"
                : editor.LoadError ?? (editor.Post.SourcePath is null ? "Drop an image here, or click to open one" : "Loading…");
            DrawCentered(context, message, bounds);
            return;
        }

        if (!ReferenceEquals(_viewportImage, bitmap))
        {
            _viewportImage = bitmap;   // a different image: start from the fit view again
            _viewport.Reset();
        }
        using var clip = context.PushClip(new Rect(Bounds.Size));
        var image = ImageRect(bitmap);
        context.DrawImage(bitmap, new Rect(bitmap.Size), image);

        var muted = new Pen(Brush("FrameBrush", Brushes.Gray), 1);
        foreach (var slide in editor.Slides.Where(s => !s.IsFull && !s.IsSelected))
        {
            var f = Frame.Of(slide, image);
            using (context.PushTransform(f.Matrix)) context.DrawRectangle(null, muted, f.Local);
            DrawBadge(context, slide.Label, f.ToCanvas(f.Local.TopLeft));
        }

        if (editor.SelectedSlide is not { } selected) return;
        var accent = Brush("AccentBrush", Brushes.Orange);
        if (selected.IsFull)
        {
            context.DrawRectangle(null, new Pen(accent, 2), image.Inflate(1));
            return;
        }

        var frame = Frame.Of(selected, image);
        var dim = Brush("DimBrush", new SolidColorBrush(Color.FromArgb(115, 0, 0, 0)));
        var outside = new StreamGeometry();
        using (var g = outside.Open())
        {
            g.SetFillRule(FillRule.EvenOdd);
            Polygon(g, [image.TopLeft, image.TopRight, image.BottomRight, image.BottomLeft]);
            Polygon(g, frame.Corners);
        }
        context.DrawGeometry(dim, null, outside);

        var editable = !editor.IsLocked;
        using (context.PushTransform(frame.Matrix))
        {
            var local = frame.Local;
            if (selected.Panels > 1)
            {
                var divider = new Pen(accent, 1, new DashStyle([4, 4], 0));
                for (var i = 1; i < selected.Panels; i++)
                {
                    var x = local.Left + i * local.Width / selected.Panels;
                    context.DrawLine(divider, new Point(x, local.Top), new Point(x, local.Bottom));
                }
            }

            context.DrawRectangle(null, new Pen(accent, 2), local);
            if (editable)
            {
                foreach (var (sx, sy) in Handles(editor.IsFreeAspect))
                {
                    var c = new Point(sx * local.Width / 2, sy * local.Height / 2);
                    context.FillRectangle(accent, new Rect(c.X - HandleSize / 2, c.Y - HandleSize / 2, HandleSize, HandleSize));
                }
                var top = new Point(0, local.Top);
                var knob = new Point(0, local.Top - RotateOffset);
                context.DrawLine(new Pen(accent, 1), top, knob);
                context.DrawEllipse(Brush("CanvasBrush", Brushes.Black), new Pen(accent, 2), knob, RotateKnob, RotateKnob);
            }
        }
        DrawBadge(context, selected.Label, frame.ToCanvas(frame.Local.TopLeft));
        if (_mode == DragMode.Rotate)
            DrawAngle(context, selected.Angle, frame.Center);
    }

    private static void Polygon(StreamGeometryContext g, IReadOnlyList<Point> points)
    {
        g.BeginFigure(points[0], true);
        foreach (var p in points.Skip(1)) g.LineTo(p);
        g.EndFigure(true);
    }

    private void DrawAngle(DrawingContext context, double angle, Point at)
    {
        var text = Text($"{angle.ToString("0.#", CultureInfo.CurrentCulture)}°", 12, Brushes.White);
        var box = new Rect(at.X - text.Width / 2 - 6, at.Y - text.Height / 2 - 2, text.Width + 12, text.Height + 4);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), box, 3);
        context.DrawText(text, new Point(box.X + 6, box.Y + 2));
    }

    /// <summary>Handle positions in frame-local units (−1…1): corners, plus edge midpoints for free frames.</summary>
    private static IEnumerable<(int X, int Y)> Handles(bool free)
    {
        (int, int)[] corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)];
        (int, int)[] edges = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        return free ? corners.Concat(edges) : corners;
    }

    /// <summary>A frame on the canvas: centre, unrotated size and angle.</summary>
    private readonly record struct Frame(Point Center, double Width, double Height, double Angle)
    {
        public static Frame Of(SlideViewModel slide, Rect image) => new(
            new Point(image.X + slide.Rect.CenterX * image.Width, image.Y + slide.Rect.CenterY * image.Height),
            slide.Rect.W * image.Width, slide.Rect.H * image.Height, slide.Angle);

        public Rect Local => new(-Width / 2, -Height / 2, Width, Height);

        public Matrix Matrix => Matrix.CreateRotation(Angle * Math.PI / 180) * Matrix.CreateTranslation(Center.X, Center.Y);

        public Point ToCanvas(Point local) => local.Transform(Matrix);

        public Point ToLocal(Point canvas)
        {
            var (c, s) = (Math.Cos(Angle * Math.PI / 180), Math.Sin(Angle * Math.PI / 180));
            var (dx, dy) = (canvas.X - Center.X, canvas.Y - Center.Y);
            return new Point(dx * c + dy * s, -dx * s + dy * c);
        }

        public bool Contains(Point canvas)
        {
            var l = ToLocal(canvas);
            return Math.Abs(l.X) <= Width / 2 && Math.Abs(l.Y) <= Height / 2;
        }

        public Point[] Corners => [ToCanvas(Local.TopLeft), ToCanvas(Local.TopRight), ToCanvas(Local.BottomRight), ToCanvas(Local.BottomLeft)];

        public Point RotateKnob => ToCanvas(new Point(0, -Height / 2 - RotateOffset));
    }

    private void DrawBadge(DrawingContext context, string label, Point at)
    {
        var text = Text(label, 11, Brushes.White);
        var box = new Rect(at.X + 6, at.Y + 6, text.Width + 10, text.Height + 2);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), box, 2);
        context.DrawText(text, new Point(box.X + 5, box.Y + 1));
    }

    private void DrawCentered(DrawingContext context, string message, Rect bounds)
    {
        var text = Text(message, 14, Brush("MutedBrush", Brushes.Gray));
        context.DrawText(text, new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2));
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), size, brush);

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    private Rect Area => new Rect(Bounds.Size).Deflate(Inset);

    private Rect ImageRect(Bitmap bitmap) => _viewport.ImageRect(Area, bitmap.Size);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Editor?.Preview is not Bitmap bitmap) return;
        _viewport.ZoomAt(e.GetPosition(this), Math.Pow(1.25, e.Delta.Y), Area, bitmap.Size);
        InvalidateVisual();
        e.Handled = true;
    }

    // ---------- interaction ----------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var editor = Editor;
        if (editor is null) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (editor.Preview is Bitmap && (props.IsMiddleButtonPressed || props.IsRightButtonPressed || (props.IsLeftButtonPressed && PanModifier)))
        {
            _mode = DragMode.Pan;
            _panLast = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        Focus();
        if (editor.Preview is not Bitmap bitmap)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var image = ImageRect(bitmap);
        var p = e.GetPosition(this);

        if (editor.SelectedSlide is { IsFull: false } selected && !editor.IsLocked)
        {
            var frame = Frame.Of(selected, image);
            if (Distance(frame.RotateKnob, p) <= HandleHit)
            {
                if (e.ClickCount == 2) { editor.RotateFrame(selected, 0, selected.Rect); e.Handled = true; return; }
                Begin(DragMode.Rotate, selected, p, e);
                return;
            }
            if (HandleAt(frame, editor.IsFreeAspect, p) is { } handle)
            {
                StartResize(editor, selected, handle);
                Begin(DragMode.Resize, selected, p, e);
                return;
            }
        }

        var hit = editor.Slides.Where(s => !s.IsFull && Frame.Of(s, image).Contains(p))
            .OrderByDescending(s => s.IsSelected)
            .FirstOrDefault();
        if (hit is not null)
        {
            editor.SelectedSlide = hit;
            if (!editor.IsLocked) Begin(DragMode.Move, hit, p, e);
        }
        else if (e.ClickCount == 2)
        {
            ResetZoom();
        }
        else if (image.Contains(p) && editor.Slides.FirstOrDefault(s => s.IsFull) is { } full)
        {
            editor.SelectedSlide = full;
        }
    }

    private static (int X, int Y)? HandleAt(Frame frame, bool free, Point p)
    {
        foreach (var (sx, sy) in Handles(free))
            if (Distance(frame.ToCanvas(new Point(sx * frame.Width / 2, sy * frame.Height / 2)), p) <= HandleHit) return (sx, sy);
        return null;
    }

    /// <summary>Remembers the handle and the opposite point, which stays put while resizing (source pixels).</summary>
    private void StartResize(EditorViewModel editor, SlideViewModel slide, (int X, int Y) handle)
    {
        _handle = handle;
        var (cx, cy) = (slide.Rect.CenterX * editor.SourceWidth, slide.Rect.CenterY * editor.SourceHeight);
        var (hw, hh) = (slide.Rect.W * editor.SourceWidth / 2, slide.Rect.H * editor.SourceHeight / 2);
        var (ex, ey) = Axes(slide.Angle);
        _anchor = new Point(cx - handle.X * hw * ex.X - handle.Y * hh * ey.X, cy - handle.X * hw * ex.Y - handle.Y * hh * ey.Y);
        _startSize = new Size(hw * 2, hh * 2);
    }

    private static (Point X, Point Y) Axes(double angle)
    {
        var (c, s) = (Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180));
        return (new Point(c, s), new Point(-s, c));
    }

    private void Begin(DragMode mode, SlideViewModel slide, Point p, PointerPressedEventArgs e)
    {
        _mode = mode;
        _dragSlide = slide;
        _dragStart = p;
        _startRect = slide.Rect;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var editor = Editor;
        if (editor?.Preview is not Bitmap bitmap) { Cursor = editor is null ? null : HandCursor; return; }
        var image = ImageRect(bitmap);
        var p = e.GetPosition(this);

        if (_mode == DragMode.Pan)
        {
            _viewport.Pan(p - _panLast, Area, bitmap.Size);
            _panLast = p;
            InvalidateVisual();
            return;
        }

        if (_mode == DragMode.None || _dragSlide is null)
        {
            Cursor = HoverCursor(editor, image, p);
            return;
        }

        switch (_mode)
        {
            case DragMode.Move:
            {
                var dx = (p.X - _dragStart.X) / image.Width;
                var dy = (p.Y - _dragStart.Y) / image.Height;
                editor.UpdateFrame(_dragSlide, _startRect with { X = _startRect.X + dx, Y = _startRect.Y + dy });
                break;
            }
            case DragMode.Rotate:
            {
                var center = new Point(image.X + _startRect.CenterX * image.Width, image.Y + _startRect.CenterY * image.Height);
                var angle = Math.Atan2(p.Y - center.Y, p.X - center.X) * 180 / Math.PI + 90;
                angle = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? Math.Round(angle / 5) * 5 : Math.Round(angle, 1);
                editor.RotateFrame(_dragSlide, angle, _startRect);
                break;
            }
            case DragMode.Resize:
                Resize(editor, _dragSlide, ToSource(p, image, editor));
                break;
        }
    }

    /// <summary>
    /// Resizes from the anchor (opposite corner or edge) towards the pointer, in the frame's rotated axes.
    /// Shrinks the dragged size, keeping the anchor fixed, until the rotated frame fits inside the image.
    /// </summary>
    private void Resize(EditorViewModel editor, SlideViewModel slide, Point pointer)
    {
        var (ex, ey) = Axes(slide.Angle);
        var d = pointer - _anchor;
        var u = d.X * ex.X + d.Y * ex.Y;
        var v = d.X * ey.X + d.Y * ey.Y;
        var (su, sv) = (u < 0 ? -1 : 1, v < 0 ? -1 : 1);
        const double min = SlideGeometry.MinFramePixels;
        var locked = editor.FrameRatio(slide);
        var w = _handle.X != 0 ? Math.Max(Math.Abs(u), min) : _startSize.Width;
        var h = _handle.Y != 0 ? Math.Max(Math.Abs(v), min) : _startSize.Height;
        if (locked is { } ratio) w = Math.Max(w, h * ratio); // locked frames only have corner handles

        // The frame scaled by t along the dragged axes, placed against the anchor.
        (double W, double H, Point C) At(double t)
        {
            var tw = _handle.X != 0 ? Math.Max(w * t, min) : w;
            var th = locked is { } r ? tw / r : _handle.Y != 0 ? Math.Max(h * t, min) : h;
            var ou = _handle.X != 0 ? su * tw / 2 : 0;
            var ov = _handle.Y != 0 ? sv * th / 2 : 0;
            return (tw, th, new Point(_anchor.X + ou * ex.X + ov * ey.X, _anchor.Y + ou * ex.Y + ov * ey.Y));
        }

        bool Fits((double W, double H, Point C) f) =>
            SlideGeometry.Inside(f.C.X, f.C.Y, f.W, f.H, slide.Angle, editor.SourceWidth, editor.SourceHeight);

        var frame = At(1);
        if (!Fits(frame))
        {
            double lo = 0, hi = 1;
            for (var i = 0; i < 24; i++)
            {
                var mid = (lo + hi) / 2;
                if (Fits(At(mid))) lo = mid; else hi = mid;
            }
            frame = At(lo);
        }
        var (sw, sh) = (editor.SourceWidth, editor.SourceHeight);
        editor.UpdateFrame(slide, new RectF((frame.C.X - frame.W / 2) / sw, (frame.C.Y - frame.H / 2) / sh, frame.W / sw, frame.H / sh));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == DragMode.None) return;
        _mode = DragMode.None;
        _dragSlide = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _mode = DragMode.None;
        _dragSlide = null;
        InvalidateVisual();
    }

    private Cursor? HoverCursor(EditorViewModel editor, Rect image, Point p)
    {
        if (PanModifier && !_viewport.IsFit) return HandCursor;
        if (editor.IsLocked) return null;
        if (editor.SelectedSlide is { IsFull: false } selected)
        {
            var frame = Frame.Of(selected, image);
            if (Distance(frame.RotateKnob, p) <= HandleHit) return RotateCursor;
            if (HandleAt(frame, editor.IsFreeAspect, p) is { } h)
                return (h.X, h.Y) switch
                {
                    (0, _) => NsCursor,
                    (_, 0) => WeCursor,
                    _ when h.X == h.Y => NwseCursor,
                    _ => NeswCursor,
                };
        }
        return editor.Slides.Any(s => !s.IsFull && Frame.Of(s, image).Contains(p)) ? MoveCursor : null;
    }

    private static Point ToSource(Point canvas, Rect image, EditorViewModel editor) =>
        new((canvas.X - image.X) / image.Width * editor.SourceWidth, (canvas.Y - image.Y) / image.Height * editor.SourceHeight);

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
