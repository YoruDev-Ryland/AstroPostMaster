using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;

namespace AstroPostMaster.Core.Tests.Slides;

public class AspectAndExportSizeTests
{
    [Theory]
    [InlineData(AspectRatio.Portrait4x5, 0.8, "4:5")]
    [InlineData(AspectRatio.Portrait3x4, 0.75, "3:4")]
    [InlineData(AspectRatio.Portrait2x3, 2.0 / 3, "2:3")]
    [InlineData(AspectRatio.Square, 1.0, "1:1")]
    [InlineData(AspectRatio.Landscape4x3, 4.0 / 3, "4:3")]
    [InlineData(AspectRatio.Landscape3x2, 1.5, "3:2")]
    [InlineData(AspectRatio.Landscape16x9, 16.0 / 9, "16:9")]
    [InlineData(AspectRatio.Landscape191x100, 1080.0 / 566, "1.91:1")]
    public void EveryAspect_HasARatioAndLabel(AspectRatio aspect, double ratio, string label)
    {
        Assert.Equal(ratio, AspectSizes.Ratio(aspect), 9);
        Assert.Equal(label, AspectSizes.Label(aspect));
    }

    [Theory]
    [InlineData(AspectRatio.Portrait2x3, 1080, 1620)]
    [InlineData(AspectRatio.Landscape4x3, 1080, 810)]
    [InlineData(AspectRatio.Landscape3x2, 1080, 720)]
    [InlineData(AspectRatio.Landscape16x9, 1080, 608)]
    [InlineData(AspectRatio.Landscape191x100, 1080, 566)]
    public void InstagramOutput_Is1080Wide(AspectRatio aspect, int w, int h) =>
        Assert.Equal((w, h), AspectSizes.Output(aspect));

    [Fact]
    public void Defaults_AreFullResolution_AndPostsFollowSettings()
    {
        Assert.Equal(ExportSize.Full, new AppSettings().ExportSize);
        Assert.Null(new Post().ExportSize);
    }

    private static readonly Slide QuarterCrop = new(SlideKind.Crop, new RectF(0.3, 0.2, 0.25, 0.5)); // 4:5 on 6000x4000: 1500x2000 px

    [Fact]
    public void Instagram1080_UsesTheFixedOutputSize() =>
        Assert.Equal((1080, 1350), SlideGeometry.OutputSize(QuarterCrop, AspectRatio.Portrait4x5, ExportSize.Instagram1080, 6000, 4000));

    [Theory]
    [InlineData(AspectRatio.Landscape16x9, 2048, 1152)]
    [InlineData(AspectRatio.Portrait4x5, 1638, 2048)]
    [InlineData(AspectRatio.Square, 2048, 2048)]
    public void LongEdge_SizesTheLongerSide(AspectRatio aspect, int w, int h)
    {
        var crop = new Slide(SlideKind.Crop, SlideGeometry.DefaultCrop(aspect, 6000, 4000));
        Assert.Equal((w, h), SlideGeometry.OutputSize(crop, aspect, ExportSize.LongEdge2048, 6000, 4000));
    }

    [Fact]
    public void Full_KeepsEverySourcePixelOfACrop() =>
        Assert.Equal((1500, 1875), SlideGeometry.OutputSize(QuarterCrop, AspectRatio.Portrait4x5, ExportSize.Full, 6000, 4000));

    [Fact]
    public void Full_NeverUpscalesASmallCrop()
    {
        var tiny = new Slide(SlideKind.Crop, new RectF(0.4, 0.4, 0.1, 0.1)); // 300 px across on 3000x2000
        var (w, _) = SlideGeometry.OutputSize(tiny, AspectRatio.Square, ExportSize.Full, 3000, 2000);
        Assert.InRange(w, 199, 300);
    }

    [Fact]
    public void Full_SplitsAPanoramaIntoEqualPanels()
    {
        var pano = new Slide(SlideKind.Panorama, SlideGeometry.DefaultPanorama(AspectRatio.Landscape16x9, 3, 6000, 4000), 3);
        Assert.Equal((2000, 1125), SlideGeometry.OutputSize(pano, AspectRatio.Landscape16x9, ExportSize.Full, 6000, 4000));
    }

    [Fact]
    public void Full_FullImageSlideFramesTheWholeImage()
    {
        Assert.Equal((6000, 7500), SlideGeometry.OutputSize(Slide.FullImage, AspectRatio.Portrait4x5, ExportSize.Full, 6000, 4000));
        Assert.Equal((6000, 4000), SlideGeometry.OutputSize(Slide.FullImage, AspectRatio.Landscape3x2, ExportSize.Full, 6000, 4000));
    }

    [Fact]
    public void Full_IsCappedAt8192OnTheLongEdge()
    {
        var (w, h) = SlideGeometry.OutputSize(Slide.FullImage, AspectRatio.Portrait4x5, ExportSize.Full, 13758, 4993);
        Assert.Equal(8192, h);
        Assert.Equal(0.8, (double)w / h, 3);
    }

    [Fact]
    public void RequiredScale_DependsOnExportSize()
    {
        Assert.Equal(1.0, SlideGeometry.RequiredScale([Slide.FullImage], AspectRatio.Portrait4x5, 6000, 4000, ExportSize.Full), 9);
        Assert.Equal(1638.0 / 6000, SlideGeometry.RequiredScale([Slide.FullImage], AspectRatio.Portrait4x5, 6000, 4000, ExportSize.LongEdge2048), 3);
    }

    [Fact]
    public void Validator_DoesNotWarnAboutUpscalingAtFullResolution()
    {
        var tiny = new Slide(SlideKind.Crop, SlideGeometry.FitLocked(new RectF(0.4, 0.4, 0.05, 0.05), 0.8, 6000, 4000));
        var post = new Post { SourcePath = "/x.jpg", Slides = [tiny] };

        Assert.DoesNotContain(PostValidator.Validate(post, "", (6000, 4000), ExportSize.Full), w => w.Kind == WarningKind.LowResolution);
        Assert.Contains(PostValidator.Validate(post, "", (6000, 4000), ExportSize.Instagram1080), w => w.Kind == WarningKind.LowResolution);
    }

    [Fact]
    public void ExportSize_RoundTripsThroughJson()
    {
        var post = new Post { ExportSize = ExportSize.LongEdge4096, Aspect = AspectRatio.Landscape16x9 };
        var json = System.Text.Json.JsonSerializer.Serialize(post, AstroPostMaster.Core.Storage.Json.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<Post>(json, AstroPostMaster.Core.Storage.Json.Options)!;
        Assert.Equal(ExportSize.LongEdge4096, back.ExportSize);
        Assert.Equal(AspectRatio.Landscape16x9, back.Aspect);
        Assert.Contains("\"Landscape16x9\"", json);
    }
}
