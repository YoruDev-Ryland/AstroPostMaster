using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using SkiaSharp;

namespace AstroPostMaster.Imaging.Tests;

public class SlideRendererTests
{
    private static LoadedImage Load(TempDir dir, string name, Action<string> write)
    {
        var path = dir.File(name);
        write(path);
        return ImageLoader.Load(path);
    }

    private static void Dispose(IEnumerable<SKBitmap> bitmaps) { foreach (var b in bitmaps) b.Dispose(); }

    [Theory]
    [InlineData(AspectRatio.Portrait4x5)]
    [InlineData(AspectRatio.Portrait3x4)]
    [InlineData(AspectRatio.Square)]
    public void Render_ProducesOutputSizeForEachAspect(AspectRatio aspect)
    {
        using var dir = new TempDir();
        using var src = Load(dir, "a.jpg", p => Fixtures.WriteRgb(p, 3000, 2000, (_, _) => (90, 90, 90)));
        var slide = new Slide(SlideKind.Crop, SlideGeometry.DefaultCrop(aspect, 3000, 2000));

        var result = SlideRenderer.Render(src, slide, aspect);

        var bitmap = Assert.Single(result);
        Assert.Equal(AspectSizes.Output(aspect), (bitmap.Width, bitmap.Height));
        Assert.True(bitmap.ColorSpace is null || bitmap.ColorSpace.IsSrgb);
        Dispose(result);
    }

    [Fact]
    public void Render_PanoramaSlicesAreContiguous()
    {
        using var dir = new TempDir();
        using var src = Load(dir, "grad.png", p => Fixtures.WriteRgb(p, 6000, 1000, (x, _) => ((byte)(x * 255 / 5999), 0, 0), SKEncodedImageFormat.Png));
        var slide = new Slide(SlideKind.Panorama, SlideGeometry.DefaultPanorama(AspectRatio.Portrait4x5, 3, 6000, 1000), 3);

        var parts = SlideRenderer.Render(src, slide, AspectRatio.Portrait4x5);

        Assert.Equal(3, parts.Count);
        for (var i = 1; i < parts.Count; i++)
        {
            var lastOfPrevious = parts[i - 1].GetPixel(1079, 675).Red;
            var firstOfNext = parts[i].GetPixel(0, 675).Red;
            Assert.InRange(Math.Abs(lastOfPrevious - firstOfNext), 0, 3);
        }
        Assert.True(parts[0].GetPixel(0, 675).Red < parts[2].GetPixel(1079, 675).Red);
        Dispose(parts);
    }

    [Fact]
    public void Render_FullSlide_LetterboxesLandscapeWithBlack()
    {
        using var dir = new TempDir();
        using var src = Load(dir, "land.png", p => Fixtures.WriteRgb(p, 3000, 2000, (_, _) => (220, 30, 30), SKEncodedImageFormat.Png));

        var bitmap = Assert.Single(SlideRenderer.Render(src, Slide.FullImage, AspectRatio.Portrait4x5));

        Assert.Equal(SKColors.Black, bitmap.GetPixel(540, 10));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(540, 1340));
        Assert.InRange(bitmap.GetPixel(540, 675).Red, 210, 230);
        bitmap.Dispose();
    }

    [Fact]
    public void Render_FullSlide_PillarboxesPortraitWithBlack()
    {
        using var dir = new TempDir();
        using var src = Load(dir, "tall.png", p => Fixtures.WriteRgb(p, 1000, 3000, (_, _) => (30, 220, 30), SKEncodedImageFormat.Png));

        var bitmap = Assert.Single(SlideRenderer.Render(src, Slide.FullImage, AspectRatio.Portrait4x5));

        Assert.Equal(SKColors.Black, bitmap.GetPixel(10, 675));
        Assert.InRange(bitmap.GetPixel(540, 675).Green, 210, 230);
        bitmap.Dispose();
    }

    [Fact]
    public void Render_ConvertsAdobeRgbToSrgb_ButLeavesSrgbAlone()
    {
        using var dir = new TempDir();
        using var adobe = Load(dir, "adobe.png", p => Fixtures.WriteRgb(p, 400, 500, (_, _) => (40, 160, 60), SKEncodedImageFormat.Png, Fixtures.AdobeRgb));
        using var srgb = Load(dir, "srgb.png", p => Fixtures.WriteRgb(p, 400, 500, (_, _) => (40, 160, 60), SKEncodedImageFormat.Png));

        var converted = Assert.Single(SlideRenderer.Render(adobe, Slide.FullImage, AspectRatio.Portrait4x5)).GetPixel(540, 675);
        var untouched = Assert.Single(SlideRenderer.Render(srgb, Slide.FullImage, AspectRatio.Portrait4x5)).GetPixel(540, 675);

        var maxShift = new[] { converted.Red - 40, converted.Green - 160, converted.Blue - 60 }.Max(Math.Abs);
        Assert.True(maxShift >= 5, $"Adobe RGB pixel was not converted: {converted}");
        Assert.InRange(untouched.Red, 38, 42);
        Assert.InRange(untouched.Green, 158, 162);
        Assert.InRange(untouched.Blue, 58, 62);
    }

    [Fact]
    public void Render_FlattensTransparencyToBlack()
    {
        using var dir = new TempDir();
        using var src = Load(dir, "alpha.png", p => Fixtures.WriteHalfTransparent(p, 400, 500));

        var bitmap = Assert.Single(SlideRenderer.Render(src, Slide.FullImage, AspectRatio.Portrait4x5));

        Assert.Equal(SKColors.Black, bitmap.GetPixel(100, 675));
        Assert.InRange(bitmap.GetPixel(900, 675).Red, 190, 210);
        bitmap.Dispose();
    }

    [Fact]
    public void Render_GrayscaleAnd16BitSourcesKeepTheirColors()
    {
        using var dir = new TempDir();
        using var gray = Load(dir, "gray.png", p => Fixtures.WriteGray(p, 400, 500, 128));
        using var deep = Load(dir, "deep.tif", p => Fixtures.WriteTiff16(p, 400, 500, 0xFFFF, 0x8000, 0));

        var g = Assert.Single(SlideRenderer.Render(gray, Slide.FullImage, AspectRatio.Portrait4x5)).GetPixel(540, 675);
        var d = Assert.Single(SlideRenderer.Render(deep, Slide.FullImage, AspectRatio.Portrait4x5)).GetPixel(540, 675);

        Assert.InRange(g.Red, 126, 130);
        Assert.Equal(g.Red, g.Green);
        Assert.Equal(g.Green, g.Blue);
        Assert.InRange(d.Red, 252, 255);
        Assert.InRange(d.Green, 125, 131);
        Assert.InRange(d.Blue, 0, 3);
    }

    [Fact]
    public void Render_UpscalesTinySourceWithoutError()
    {
        using var dir = new TempDir();
        using var src = Load(dir, "tiny.jpg", p => Fixtures.WriteRgb(p, 300, 200, (_, _) => (50, 60, 70)));
        var slide = new Slide(SlideKind.Crop, SlideGeometry.DefaultCrop(AspectRatio.Portrait4x5, 300, 200));

        var bitmap = Assert.Single(SlideRenderer.Render(src, slide, AspectRatio.Portrait4x5));

        Assert.Equal((1080, 1350), (bitmap.Width, bitmap.Height));
        bitmap.Dispose();
    }

    [Fact]
    public void Render_Watermark_OnlyTouchesItsCorner()
    {
        using var dir = new TempDir();
        using var src = Load(dir, "black.png", p => Fixtures.WriteRgb(p, 1080, 1350, (_, _) => (0, 0, 0), SKEncodedImageFormat.Png));
        var watermark = new WatermarkSettings { Enabled = true, Text = "TEST HANDLE", Corner = Corner.BottomRight, SizeFraction = 0.05, Opacity = 1 };

        var bitmap = Assert.Single(SlideRenderer.Render(src, Slide.FullImage, AspectRatio.Portrait4x5, watermark));

        int Bright(int x0, int y0, int x1, int y1)
        {
            var n = 0;
            for (var y = y0; y < y1; y += 2)
                for (var x = x0; x < x1; x += 2)
                    if (bitmap.GetPixel(x, y).Red > 100) n++;
            return n;
        }
        Assert.True(Bright(540, 1150, 1080, 1350) > 0, "watermark not drawn in bottom-right");
        Assert.Equal(0, Bright(0, 0, 540, 675));
        bitmap.Dispose();
    }

    [Fact]
    public void Render_TiffWithStraightAlpha_IsNotDarkenedTwice()
    {
        using var dir = new TempDir();
        using var src = Load(dir, "alpha.tif", p => Fixtures.WriteTiffRgba8(p, 400, 500, 200, 200, 200, 128));

        var c = Assert.Single(SlideRenderer.Render(src, Slide.FullImage, AspectRatio.Portrait4x5)).GetPixel(540, 675);

        Assert.InRange(c.Red, 92, 108); // 200 × 50 % over black, not 200 × 25 %
    }

    [Fact]
    public void Render_ClampsOffEdgeCropInsteadOfStretchingIt()
    {
        using var dir = new TempDir();
        using var src = Load(dir, "split.png", p => Fixtures.WriteRgb(p, 1000, 1000, (x, _) => x < 900 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0), SKEncodedImageFormat.Png));
        var offEdge = new Slide(SlideKind.Crop, new RectF(0.9, 0.1, 0.3, 0.4));

        var bitmap = Assert.Single(SlideRenderer.Render(src, offEdge, AspectRatio.Portrait4x5));

        Assert.True(bitmap.GetPixel(100, 675).Red > 200, "frame should be moved inside the image, showing the white area");
        Assert.True(bitmap.GetPixel(1070, 675).Red < 50);
        bitmap.Dispose();
    }
}
