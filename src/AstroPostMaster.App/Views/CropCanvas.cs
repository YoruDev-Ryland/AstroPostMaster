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
/// resize (ratio locked). The selected frame is the only accent-coloured element; everything outside it is dimmed.
/// </summary>
public sealed class CropCanvas : Control, ICustomHitTest
{
    public static readonly StyledProperty<EditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<CropCanvas, EditorViewModel?>(nameof(Editor));

    private const double Inset = 16;
    private const double HandleSize = 9;
    private const double HandleHit = 12;

    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor NwseCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor NeswCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private enum DragMode { None, Move, Resize, Pan }

    private readonly Viewport _viewport = new();
    private object? _viewportImage;
    private Point _panLast;

    private DragMode _mode;
    private SlideViewModel? _dragSlide;
    private Point _dragStart;
    private RectF _startRect;
    private Point _anchor; // resize anchor in source pixels

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
            var r = ToCanvas(slide.Rect, image);
            context.DrawRectangle(null, muted, r);
            DrawBadge(context, slide.Label, r.TopLeft);
        }

        if (editor.SelectedSlide is not { } selected) return;
        var accent = Brush("AccentBrush", Brushes.Orange);
        if (selected.IsFull)
        {
            context.DrawRectangle(null, new Pen(accent, 2), image.Inflate(1));
            return;
        }

        var frame = ToCanvas(selected.Rect, image);
        var dim = Brush("DimBrush", new SolidColorBrush(Color.FromArgb(115, 0, 0, 0)));
        context.FillRectangle(dim, new Rect(image.Left, image.Top, image.Width, Math.Max(0, frame.Top - image.Top)));
        context.FillRectangle(dim, new Rect(image.Left, frame.Bottom, image.Width, Math.Max(0, image.Bottom - frame.Bottom)));
        context.FillRectangle(dim, new Rect(image.Left, frame.Top, Math.Max(0, frame.Left - image.Left), frame.Height));
        context.FillRectangle(dim, new Rect(frame.Right, frame.Top, Math.Max(0, image.Right - frame.Right), frame.Height));

        if (selected.Panels > 1)
        {
            var divider = new Pen(accent, 1, new DashStyle([4, 4], 0));
            foreach (var panel in selected.PanelRects.Skip(1))
            {
                var x = image.X + panel.X * image.Width;
                context.DrawLine(divider, new Point(x, frame.Top), new Point(x, frame.Bottom));
            }
        }

        context.DrawRectangle(null, new Pen(accent, 2), frame);
        foreach (var corner in Corners(frame))
            context.FillRectangle(accent, new Rect(corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize));
        DrawBadge(context, selected.Label, frame.TopLeft);
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

    private static Rect ToCanvas(RectF r, Rect image) =>
        new(image.X + r.X * image.Width, image.Y + r.Y * image.Height, r.W * image.Width, r.H * image.Height);

    private static Point[] Corners(Rect r) => [r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft];

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
            var corners = Corners(ToCanvas(selected.Rect, image));
            for (var i = 0; i < corners.Length; i++)
            {
                if (Distance(corners[i], p) > HandleHit) continue;
                var opposite = corners[(i + 2) % 4];
                _anchor = ToSource(opposite, image, editor);
                Begin(DragMode.Resize, selected, p, e);
                return;
            }
        }

        var hit = editor.Slides.Where(s => !s.IsFull && ToCanvas(s.Rect, image).Contains(p))
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

        if (_mode == DragMode.Move)
        {
            var dx = (p.X - _dragStart.X) / image.Width;
            var dy = (p.Y - _dragStart.Y) / image.Height;
            editor.UpdateFrame(_dragSlide, _startRect with { X = _startRect.X + dx, Y = _startRect.Y + dy });
            return;
        }

        var ratio = SlideGeometry.FrameRatio(editor.Aspect, _dragSlide.Panels);
        var pointer = ToSource(p, image, editor);
        var px = Math.Clamp(pointer.X, 0, editor.SourceWidth);
        var py = Math.Clamp(pointer.Y, 0, editor.SourceHeight);
        var w = Math.Max(Math.Abs(px - _anchor.X), Math.Abs(py - _anchor.Y) * ratio);
        var h = w / ratio;
        var x = px < _anchor.X ? _anchor.X - w : _anchor.X;
        var y = py < _anchor.Y ? _anchor.Y - h : _anchor.Y;
        editor.UpdateFrame(_dragSlide, new RectF(x / editor.SourceWidth, y / editor.SourceHeight, w / editor.SourceWidth, h / editor.SourceHeight));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == DragMode.None) return;
        _mode = DragMode.None;
        _dragSlide = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _mode = DragMode.None;
        _dragSlide = null;
    }

    private Cursor? HoverCursor(EditorViewModel editor, Rect image, Point p)
    {
        if (PanModifier && !_viewport.IsFit) return HandCursor;
        if (editor.IsLocked) return null;
        if (editor.SelectedSlide is { IsFull: false } selected)
        {
            var corners = Corners(ToCanvas(selected.Rect, image));
            for (var i = 0; i < corners.Length; i++)
                if (Distance(corners[i], p) <= HandleHit) return i % 2 == 0 ? NwseCursor : NeswCursor;
        }
        return editor.Slides.Any(s => !s.IsFull && ToCanvas(s.Rect, image).Contains(p)) ? MoveCursor : null;
    }

    private static Point ToSource(Point canvas, Rect image, EditorViewModel editor) =>
        new((canvas.X - image.X) / image.Width * editor.SourceWidth, (canvas.Y - image.Y) / image.Height * editor.SourceHeight);

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
