using Avalonia;

namespace AstroPostMaster.App.Views;

/// <summary>
/// Zoom and pan for the crop canvas. Zoom 1 is the "fit with a gap" view and is also the minimum, so zooming
/// out always lands back exactly there. The image never pans past its own edges.
/// </summary>
public sealed class Viewport
{
    public const double MaxZoom = 8;

    private double _centerX = 0.5; // image fraction shown at the middle of the area
    private double _centerY = 0.5;

    public double Zoom { get; private set; } = 1;
    public bool IsFit => Zoom <= 1 + 1e-9;

    public void Reset()
    {
        Zoom = 1;
        _centerX = _centerY = 0.5;
    }

    public Rect ImageRect(Rect area, Size image)
    {
        if (area.Width <= 0 || area.Height <= 0 || image.Width <= 0 || image.Height <= 0) return default;
        var (w, h) = Scaled(area, image);
        return new Rect(Axis(area.X, area.Width, w, _centerX), Axis(area.Y, area.Height, h, _centerY), w, h);
    }

    /// <summary>Zooms by <paramref name="factor"/> keeping the image point under <paramref name="cursor"/> still.</summary>
    public void ZoomAt(Point cursor, double factor, Rect area, Size image)
    {
        var before = ImageRect(area, image);
        if (before.Width <= 0) return;
        var zoom = Math.Clamp(Zoom * factor, 1, MaxZoom);
        if (Math.Abs(zoom - Zoom) < 1e-12) return;
        var fx = (cursor.X - before.X) / before.Width;
        var fy = (cursor.Y - before.Y) / before.Height;

        Zoom = zoom;
        if (IsFit)
        {
            Reset();
            return;
        }
        var (w, h) = Scaled(area, image);
        _centerX = (area.X + area.Width / 2 - (cursor.X - fx * w)) / w;
        _centerY = (area.Y + area.Height / 2 - (cursor.Y - fy * h)) / h;
        StoreClamped(area, image);
    }

    public void Pan(Vector delta, Rect area, Size image)
    {
        var r = ImageRect(area, image);
        if (r.Width <= 0) return;
        _centerX -= delta.X / r.Width;
        _centerY -= delta.Y / r.Height;
        StoreClamped(area, image);
    }

    private (double W, double H) Scaled(Rect area, Size image)
    {
        var fit = Math.Min(area.Width / image.Width, area.Height / image.Height);
        return (image.Width * fit * Zoom, image.Height * fit * Zoom);
    }

    private static double Axis(double start, double length, double size, double center)
    {
        if (size <= length) return start + (length - size) / 2;
        return Math.Clamp(start + length / 2 - center * size, start + length - size, start);
    }

    private void StoreClamped(Rect area, Size image)
    {
        var r = ImageRect(area, image);
        _centerX = (area.X + area.Width / 2 - r.X) / r.Width;
        _centerY = (area.Y + area.Height / 2 - r.Y) / r.Height;
    }
}
