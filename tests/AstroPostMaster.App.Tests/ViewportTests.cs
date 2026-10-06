using AstroPostMaster.App.Views;
using Avalonia;

namespace AstroPostMaster.App.Tests;

public class ViewportTests
{
    private static readonly Rect Area = new(16, 16, 1000, 600);
    private static readonly Size Image = new(3000, 2000); // 1.5:1 inside a 1.667:1 area → height-limited

    private static Rect Fit()
    {
        var scale = Math.Min(Area.Width / Image.Width, Area.Height / Image.Height);
        var (w, h) = (Image.Width * scale, Image.Height * scale);
        return new Rect(Area.X + (Area.Width - w) / 2, Area.Y + (Area.Height - h) / 2, w, h);
    }

    private static void Near(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }

    [Fact]
    public void AtZoomOne_TheImageFitsExactlyAsBefore() => Near(Fit(), new Viewport().ImageRect(Area, Image));

    [Fact]
    public void CannotZoomOutPastTheFit()
    {
        var v = new Viewport();
        v.ZoomAt(new Point(300, 300), 0.5, Area, Image);
        Assert.Equal(1, v.Zoom);
        Near(Fit(), v.ImageRect(Area, Image));
    }

    [Fact]
    public void ZoomingKeepsThePointUnderTheCursorStill()
    {
        var v = new Viewport();
        var cursor = new Point(400, 250);
        var before = v.ImageRect(Area, Image);
        var fx = (cursor.X - before.X) / before.Width;
        var fy = (cursor.Y - before.Y) / before.Height;

        v.ZoomAt(cursor, 2.5, Area, Image);

        var after = v.ImageRect(Area, Image);
        Assert.Equal(2.5, v.Zoom, 9);
        Assert.Equal(cursor.X, after.X + fx * after.Width, 6);
        Assert.Equal(cursor.Y, after.Y + fy * after.Height, 6);
    }

    [Fact]
    public void ZoomIsCappedAtEightTimes()
    {
        var v = new Viewport();
        for (var i = 0; i < 20; i++) v.ZoomAt(new Point(500, 300), 2, Area, Image);
        Assert.Equal(Viewport.MaxZoom, v.Zoom);
    }

    [Fact]
    public void PanningStopsAtTheImageEdges()
    {
        var v = new Viewport();
        v.ZoomAt(new Point(516, 316), 4, Area, Image);
        v.Pan(new Vector(100000, 100000), Area, Image);
        var r = v.ImageRect(Area, Image);
        Assert.Equal(Area.Left, r.Left, 6);
        Assert.Equal(Area.Top, r.Top, 6);

        v.Pan(new Vector(-100000, -100000), Area, Image);
        r = v.ImageRect(Area, Image);
        Assert.Equal(Area.Right, r.Right, 6);
        Assert.Equal(Area.Bottom, r.Bottom, 6);
    }

    [Fact]
    public void PanningAtZoomOne_DoesNothing()
    {
        var v = new Viewport();
        v.Pan(new Vector(200, -150), Area, Image);
        Near(Fit(), v.ImageRect(Area, Image));
    }

    [Fact]
    public void Reset_ReturnsToTheFit()
    {
        var v = new Viewport();
        v.ZoomAt(new Point(100, 100), 3, Area, Image);
        v.Reset();
        Assert.True(v.IsFit);
        Near(Fit(), v.ImageRect(Area, Image));
    }
}
