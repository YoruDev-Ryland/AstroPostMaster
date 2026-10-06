using AstroPostMaster.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AstroPostMaster.App.Views;

/// <summary>Slide thumbnails. Click selects; drag horizontally to reorder.</summary>
public partial class Filmstrip : UserControl
{
    private const double DragThreshold = 6;
    private SlideViewModel? _pressed;
    private Control? _pressedContainer;
    private Point _start;
    private bool _dragging;

    public Filmstrip()
    {
        InitializeComponent();
        Strip.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        Strip.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        Strip.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
    }

    private EditorViewModel? Editor => DataContext as EditorViewModel;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Editor is not { } editor || (e.Source as Control)?.DataContext is not SlideViewModel slide) return;
        editor.SelectedSlide = slide;
        _pressed = slide;
        _pressedContainer = Strip.ContainerFromIndex(editor.Slides.IndexOf(slide));
        _start = e.GetPosition(Strip);
        _dragging = false;
        e.Pointer.Capture(Strip);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null) return;
        if (!_dragging && Math.Abs(e.GetPosition(Strip).X - _start.X) > DragThreshold)
        {
            _dragging = true;
            if (_pressedContainer is not null) _pressedContainer.Opacity = 0.55;
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressed is { } slide && _dragging && Editor is { } editor)
        {
            var from = editor.Slides.IndexOf(slide);
            var slot = InsertionSlot(e.GetPosition(Strip).X, editor.Slides.Count);
            editor.MoveSlide(from, Math.Min(EditorViewModel.DropIndex(from, slot), editor.Slides.Count - 1));
        }
        if (_pressedContainer is not null) _pressedContainer.Opacity = 1;
        _pressed = null;
        _pressedContainer = null;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>Insertion slot 0..count: the number of thumbnails whose centre lies left of the pointer.</summary>
    private int InsertionSlot(double x, int count)
    {
        var slot = 0;
        for (var i = 0; i < count; i++)
        {
            if (Strip.ContainerFromIndex(i) is not { } container) continue;
            var left = container.TranslatePoint(new Point(0, 0), Strip)?.X ?? 0;
            if (x > left + container.Bounds.Width / 2) slot = i + 1;
        }
        return slot;
    }
}
