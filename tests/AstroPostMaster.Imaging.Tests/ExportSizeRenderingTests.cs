using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using SkiaSharp;

namespace AstroPostMaster.Imaging.Tests;

public class ExportSizeRenderingTests
{
    private static LoadedImage Source(TempDir dir, int w = 3000, int h = 2000)
    {
        var path = dir.File("src.png");
        Fixtures.WriteRgb(path, w, h, (x, _) => ((byte)(x * 255 / (w - 1)), 60, 90), SKEncodedImageFormat.Png);
        return ImageLoader.Load(path);
    }

    [Fact]
    public void FullResolution_CropKeepsItsSourcePixels()
    {
        using var dir = new TempDir();
        using var src = Source(dir);
        var crop = new Slide(SlideKind.Crop, new RectF(0.3, 0.2, 0.25, 0.5)); // 750 px wide, 4:5

        var bitmap = Assert.Single(SlideRenderer.Render(src, crop, AspectRatio.Portrait4x5, size: ExportSize.Full));

        Assert.Equal((750, 938), (bitmap.Width, bitmap.Height));
        bitmap.Dispose();
    }

    [Theory]
    [InlineData(AspectRatio.Landscape16x9, 1080, 608)]
    [InlineData(AspectRatio.Landscape191x100, 1080, 566)]
    [InlineData(AspectRatio.Portrait2x3, 1080, 1620)]
    public void Instagram1080_RendersNewShapes(AspectRatio aspect, int w, int h)
    {
        using var dir = new TempDir();
        using var src = Source(dir);
        var crop = new Slide(SlideKind.Crop, SlideGeometry.DefaultCrop(aspect, 3000, 2000));

        var bitmap = Assert.Single(SlideRenderer.Render(src, crop, aspect, size: ExportSize.Instagram1080));

        Assert.Equal((w, h), (bitmap.Width, bitmap.Height));
        bitmap.Dispose();
    }

    [Fact]
    public void FullResolution_LandscapePanoramaPanelsAreEqualAndContiguous()
    {
        using var dir = new TempDir();
        using var src = Source(dir);
        var pano = new Slide(SlideKind.Panorama, SlideGeometry.DefaultPanorama(AspectRatio.Landscape3x2, 2, 3000, 2000), 2);

        var parts = SlideRenderer.Render(src, pano, AspectRatio.Landscape3x2, size: ExportSize.Full);

        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal((1500, 1000), (p.Width, p.Height)));
        var seam = Math.Abs(parts[0].GetPixel(1499, 500).Red - parts[1].GetPixel(0, 500).Red);
        Assert.InRange(seam, 0, 3);
        foreach (var p in parts) p.Dispose();
    }

    [Fact]
    public void Exporter_UsesThePostOverrideBeforeTheSetting()
    {
        using var dir = new TempDir();
        var source = dir.File("src.jpg");
        Fixtures.WriteRgb(source, 3000, 2000, (_, _) => (40, 50, 60));
        var post = new Post
        {
            SourcePath = source,
            Aspect = AspectRatio.Landscape16x9,
            ExportSize = ExportSize.LongEdge2048,
            Slides = [Slide.FullImage],
        };

        var result = PostExporter.Export(post, "c", new AppSettings { ExportSize = ExportSize.Instagram1080 }, dir.File("export"));

        using var bitmap = SKBitmap.Decode(result.SlideFiles[0]);
        Assert.Equal((2048, 1152), (bitmap.Width, bitmap.Height));
    }

    [Fact]
    public void Exporter_DefaultsToFullResolution()
    {
        using var dir = new TempDir();
        var source = dir.File("src.jpg");
        Fixtures.WriteRgb(source, 3000, 2000, (_, _) => (40, 50, 60));
        var post = new Post { SourcePath = source, Aspect = AspectRatio.Landscape3x2, Slides = [Slide.FullImage] };

        var result = PostExporter.Export(post, "c", new AppSettings(), dir.File("export"));

        using var bitmap = SKBitmap.Decode(result.SlideFiles[0]);
        Assert.Equal((3000, 2000), (bitmap.Width, bitmap.Height));
    }
}
