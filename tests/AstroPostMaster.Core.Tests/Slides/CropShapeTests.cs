using System.Text.Json;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.Core.Tests.Slides;

public class CropShapeTests
{
    [Theory]
    [InlineData("5:4", 5, 4)]
    [InlineData(" 16 x 9 ", 16, 9)]
    [InlineData("3/2", 3, 2)]
    [InlineData("1.25", 1.25, 1)]
    [InlineData("2,5:1", 2.5, 1)]
    public void TryParse_AcceptsCommonForms(string text, double w, double h)
    {
        Assert.True(PostShape.TryParse(text, out var pw, out var ph));
        Assert.Equal((w, h), (pw, ph));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0:1")]
    [InlineData("-4:5")]
    [InlineData("100:1")]
    [InlineData("1:2:3")]
    public void TryParse_RejectsNonsense(string text) => Assert.False(PostShape.TryParse(text, out _, out _));

    [Fact]
    public void Fit_WithoutRotationMatchesFitLocked()
    {
        var rect = new RectF(0.7, 0.6, 0.5, 0.5);
        var a = SlideGeometry.FitLocked(rect, 0.8, 6000, 4000);
        var b = SlideGeometry.Fit(rect, 0.8, 6000, 4000, 0);
        Assert.Equal(a.X, b.X, 9);
        Assert.Equal(a.Y, b.Y, 9);
        Assert.Equal(a.W, b.W, 9);
        Assert.Equal(a.H, b.H, 9);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(-30)]
    [InlineData(90)]
    [InlineData(170)]
    public void Fit_KeepsRotatedFrameInsideTheImage(double angle)
    {
        var fitted = SlideGeometry.Fit(new RectF(0, 0, 1, 1), 1.5, 6000, 4000, angle);

        foreach (var (x, y) in SlideGeometry.Corners(fitted, angle, 6000, 4000))
        {
            Assert.InRange(x, -1e-6, 6000 + 1e-6);
            Assert.InRange(y, -1e-6, 4000 + 1e-6);
        }
        Assert.Equal(1.5, fitted.W * 6000 / (fitted.H * 4000), 6);
    }

    [Fact]
    public void Fit_FreeKeepsTheFramesOwnShape()
    {
        var rect = new RectF(0.1, 0.1, 0.3, 0.1); // 1800×400 px
        var fitted = SlideGeometry.Fit(rect, null, 6000, 4000, 0);
        Assert.Equal((rect.X, rect.Y, rect.W, rect.H), (Math.Round(fitted.X, 9), Math.Round(fitted.Y, 9), Math.Round(fitted.W, 9), Math.Round(fitted.H, 9)));
    }

    [Fact]
    public void SplitPanorama_RotatedPanelsFollowTheBand()
    {
        var band = new RectF(0.25, 0.4, 0.5, 0.2);
        Assert.Equal(SlideGeometry.SplitPanorama(band, 3), SlideGeometry.SplitPanorama(band, 3, 0, 6000, 4000));

        var panels = SlideGeometry.SplitPanorama(band, 2, 90, 6000, 4000);
        // Rotated 90° clockwise, the band runs downwards: panel 1 above the centre, panel 2 below.
        Assert.Equal(band.CenterX, panels[0].CenterX, 9);
        Assert.Equal(band.CenterX, panels[1].CenterX, 9);
        Assert.True(panels[0].CenterY < band.CenterY && panels[1].CenterY > band.CenterY);
    }

    [Theory]
    [InlineData(370, 10)]
    [InlineData(-190, 170)]
    [InlineData(180, 180)]
    [InlineData(-180, 180)]
    public void NormalizeAngle_WrapsToHalfOpenRange(double angle, double expected) =>
        Assert.Equal(expected, SlideGeometry.NormalizeAngle(angle), 9);

    [Fact]
    public void Validate_WarnsWhenCustomShapeIsOutsideInstagram()
    {
        var post = new Post { SourcePath = "/x.jpg", Aspect = AspectRatio.Custom, CustomAspectWidth = 9, CustomAspectHeight = 16, Slides = [Slide.FullImage] };
        Assert.Contains(PostValidator.Validate(post, "", (6000, 4000)), w => w.Kind == WarningKind.ShapeOutsideInstagram);

        post.CustomAspectWidth = 5;
        post.CustomAspectHeight = 4;
        Assert.Empty(PostValidator.Validate(post, "", (6000, 4000)));
    }

    [Fact]
    public void Validate_WarnsWhenFreeSlidesHaveDifferentShapes()
    {
        var square = new Slide(SlideKind.Crop, new RectF(0, 0, 0.5, 0.75));      // 3000×3000
        var wide = new Slide(SlideKind.Crop, new RectF(0, 0, 0.5, 0.5));         // 3000×2000
        var post = new Post { SourcePath = "/x.jpg", Aspect = AspectRatio.Free, Slides = [square, square] };
        Assert.DoesNotContain(PostValidator.Validate(post, "", (6000, 4000)), w => w.Kind == WarningKind.MixedShapes);

        post.Slides = [square, wide];
        Assert.Contains(PostValidator.Validate(post, "", (6000, 4000)), w => w.Kind == WarningKind.MixedShapes);
    }

    [Fact]
    public void Validate_RotatedFrameInsideTheImageIsNotAdjusted()
    {
        var rect = SlideGeometry.Fit(new RectF(0.3, 0.3, 0.3, 0.3), 0.8, 6000, 4000, 20);
        var post = new Post { SourcePath = "/x.jpg", Slides = [new Slide(SlideKind.Crop, rect, Angle: 20)] };
        Assert.DoesNotContain(PostValidator.Validate(post, "", (6000, 4000)), w => w.Kind == WarningKind.FrameAdjusted);
    }

    [Fact]
    public void Json_RoundTripsAngleAndCustomShape_AndOldPostsLoadUnrotated()
    {
        var post = new Post { Aspect = AspectRatio.Custom, CustomAspectWidth = 5, CustomAspectHeight = 4,
            Slides = [new Slide(SlideKind.Crop, new RectF(0.1, 0.1, 0.2, 0.2), Angle: 12.5)] };
        var back = JsonSerializer.Deserialize<Post>(JsonSerializer.Serialize(post, Json.Options), Json.Options)!;
        Assert.Equal(AspectRatio.Custom, back.Aspect);
        Assert.Equal((5.0, 4.0), (back.CustomAspectWidth, back.CustomAspectHeight));
        Assert.Equal(12.5, back.Slides[0].Angle);

        var old = JsonSerializer.Deserialize<Slide>("""{"kind":"Panorama","rect":{"x":0,"y":0,"w":1,"h":0.5},"panels":3}""", Json.Options)!;
        Assert.Equal(0, old.Angle);
        Assert.Equal(3, old.Panels);
    }
}
