using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;

namespace AstroPostMaster.Core.Tests.Slides;

public class SlideGeometryTests
{
    private const double Eps = 1e-9;

    private static double PixelRatio(RectF r, int w, int h) => r.W * w / (r.H * h);

    private static void AssertInside(RectF r)
    {
        Assert.InRange(r.X, -Eps, 1);
        Assert.InRange(r.Y, -Eps, 1);
        Assert.InRange(r.Right, 0, 1 + Eps);
        Assert.InRange(r.Bottom, 0, 1 + Eps);
    }

    [Theory]
    [InlineData(AspectRatio.Portrait4x5, 1080, 1350)]
    [InlineData(AspectRatio.Portrait3x4, 1080, 1440)]
    [InlineData(AspectRatio.Square, 1080, 1080)]
    public void AspectSizes_MatchInstagram(AspectRatio aspect, int w, int h) =>
        Assert.Equal((w, h), AspectSizes.Output(aspect));

    [Fact]
    public void DefaultCrop_IsCenteredHalfOfLargestFrame()
    {
        var r = SlideGeometry.DefaultCrop(AspectRatio.Portrait4x5, 6000, 4000);
        Assert.Equal(1600.0 / 6000, r.W, 9);
        Assert.Equal(0.5, r.H, 9);
        Assert.Equal(0.5, r.CenterX, 9);
        Assert.Equal(0.5, r.CenterY, 9);
        Assert.Equal(0.8, PixelRatio(r, 6000, 4000), 9);
    }

    [Fact]
    public void DefaultPanorama_UsesFullWidthWhenImageIsNarrowerThanBand()
    {
        var r = SlideGeometry.DefaultPanorama(AspectRatio.Portrait4x5, 3, 6000, 4000);
        Assert.Equal(0.0, r.X, 9);
        Assert.Equal(0.1875, r.Y, 9);
        Assert.Equal(1.0, r.W, 9);
        Assert.Equal(0.625, r.H, 9);
    }

    [Fact]
    public void DefaultPanorama_FitsAWideBandOnAPortraitImage()
    {
        var r = SlideGeometry.DefaultPanorama(AspectRatio.Portrait4x5, 5, 4000, 6000);
        AssertInside(r);
        Assert.Equal(4.0, PixelRatio(r, 4000, 6000), 9);
        Assert.Equal(1.0, r.W, 9);
    }

    [Fact]
    public void FitLocked_KeepsWidthAndCentreWhenItFits()
    {
        var r = SlideGeometry.FitLocked(new RectF(0.1, 0.1, 0.2, 0.9), 0.8, 6000, 4000);
        Assert.Equal(0.1, r.X, 9);
        Assert.Equal(0.2, r.W, 9);
        Assert.Equal(0.375, r.H, 9);
        Assert.Equal(0.55, r.CenterY, 9);
    }

    [Fact]
    public void FitLocked_ClampsOversizeAndOffEdgeRects()
    {
        var r = SlideGeometry.FitLocked(new RectF(0.8, 0.8, 0.9, 0.9), 0.8, 6000, 4000);
        AssertInside(r);
        Assert.Equal(0.8, PixelRatio(r, 6000, 4000), 9);
        Assert.Equal(1.0, r.H, 9);
        Assert.Equal(1.0, r.Right, 9);

        var left = SlideGeometry.FitLocked(new RectF(-0.5, -0.5, 0.2, 0.2), 0.8, 6000, 4000);
        AssertInside(left);
        Assert.Equal(0.0, left.X, 9);
        Assert.Equal(0.0, left.Y, 9);
    }

    [Fact]
    public void FitLocked_HandlesImagesSmallerThanMinimumFrame()
    {
        var r = SlideGeometry.FitLocked(new RectF(0.4, 0.4, 0.01, 0.01), 1.0, 10, 10);
        Assert.Equal(0.0, r.X, 9);
        Assert.Equal(0.0, r.Y, 9);
        Assert.Equal(1.0, r.W, 9);
        Assert.Equal(1.0, r.H, 9);
    }

    [Fact]
    public void SplitPanorama_IsContiguous()
    {
        var parts = SlideGeometry.SplitPanorama(new RectF(0.1, 0.2, 0.6, 0.3), 3);
        Assert.Equal(3, parts.Count);
        Assert.Equal(0.1, parts[0].X, 9);
        for (var i = 1; i < parts.Count; i++) Assert.Equal(parts[i - 1].Right, parts[i].X, 9);
        Assert.Equal(0.7, parts[^1].Right, 9);
        Assert.All(parts, p => Assert.Equal(0.3, p.H, 9));
    }

    [Fact]
    public void ToPixels_ClampsToImage()
    {
        Assert.Equal((90, 90, 10, 10), SlideGeometry.ToPixels(new RectF(0.9, 0.9, 0.5, 0.5), 100, 100));
        Assert.Equal((0, 0, 1, 1), SlideGeometry.ToPixels(new RectF(-1, -1, 0, 0), 100, 100));
    }

    [Fact]
    public void FitInside_LetterboxesAndPillarboxes()
    {
        Assert.Equal((0.0, 315.0, 1080.0, 720.0), SlideGeometry.FitInside(6000, 4000, 1080, 1350));
        Assert.Equal((315.0, 0.0, 450.0, 1350.0), SlideGeometry.FitInside(1000, 3000, 1080, 1350));
    }

    [Fact]
    public void RequiredScale_IsDrivenByTheTightestCrop()
    {
        var crop = new Slide(SlideKind.Crop, new RectF(0, 0, 0.25, 0.4));
        Assert.Equal(0.72, SlideGeometry.RequiredScale([crop, Slide.FullImage], AspectRatio.Portrait4x5, 6000, 4000), 9);
        Assert.Equal(0.18, SlideGeometry.RequiredScale([Slide.FullImage], AspectRatio.Portrait4x5, 6000, 4000), 9);
        Assert.Equal(1.0, SlideGeometry.RequiredScale([crop], AspectRatio.Portrait4x5, 500, 400), 9);
        Assert.Equal(1.0, SlideGeometry.RequiredScale([], AspectRatio.Portrait4x5, 500, 400), 9);
    }
}
