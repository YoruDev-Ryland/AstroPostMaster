using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using SkiaSharp;

namespace AstroPostMaster.Imaging.Tests;

public class RotatedRenderingTests
{
    // Top half red, bottom half blue.
    private static LoadedImage Split(TempDir dir)
    {
        var path = dir.File("split.png");
        Fixtures.WriteRgb(path, 2000, 2000, (_, y) => y < 1000 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255),
            SKEncodedImageFormat.Png);
        return ImageLoader.Load(path);
    }

    private static Post Square(Slide slide) => new() { Aspect = AspectRatio.Square, Slides = [slide] };

    [Fact]
    public void Render_UnrotatedSquareShowsRedOverBlue()
    {
        using var dir = new TempDir();
        using var src = Split(dir);
        var slide = new Slide(SlideKind.Crop, new RectF(0.25, 0.25, 0.5, 0.5));

        var bitmap = Assert.Single(SlideRenderer.Render(src, Square(slide), slide));

        Assert.True(bitmap.GetPixel(540, 100).Red > 200);
        Assert.True(bitmap.GetPixel(540, 980).Blue > 200);
        bitmap.Dispose();
    }

    [Fact]
    public void Render_FrameTurnedClockwiseShowsTheImageTurnedBack()
    {
        using var dir = new TempDir();
        using var src = Split(dir);
        var slide = new Slide(SlideKind.Crop, new RectF(0.25, 0.25, 0.5, 0.5), Angle: 90);

        var bitmap = Assert.Single(SlideRenderer.Render(src, Square(slide), slide));

        // The frame's right edge lies below the centre, its left edge above.
        Assert.Equal((1080, 1080), (bitmap.Width, bitmap.Height));
        Assert.True(bitmap.GetPixel(100, 540).Red > 200);
        Assert.True(bitmap.GetPixel(980, 540).Blue > 200);
        bitmap.Dispose();
    }

    [Fact]
    public void Render_CustomAndFreeShapesSetTheOutputSize()
    {
        using var dir = new TempDir();
        using var src = Split(dir);
        var slide = new Slide(SlideKind.Crop, new RectF(0.1, 0.1, 0.5, 0.25));

        var custom = new Post { Aspect = AspectRatio.Custom, CustomAspectWidth = 5, CustomAspectHeight = 4, Slides = [slide] };
        var a = Assert.Single(SlideRenderer.Render(src, custom, slide));
        Assert.Equal((1080, 864), (a.Width, a.Height));

        var free = new Post { Aspect = AspectRatio.Free, Slides = [slide] };
        var b = Assert.Single(SlideRenderer.Render(src, free, slide));
        Assert.Equal((1080, 540), (b.Width, b.Height));
        a.Dispose();
        b.Dispose();
    }
}
