using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;

namespace AstroPostMaster.Core.Tests.Slides;

public class PostValidatorTests
{
    private static Post PostWith(params Slide[] slides) => new() { SourcePath = "/x.jpg", Slides = [.. slides] };

    [Fact]
    public void Validate_CleanPostHasNoWarnings() =>
        Assert.Empty(PostValidator.Validate(PostWith(Slide.FullImage), "caption #a", (6000, 4000)));

    [Fact]
    public void Validate_WarnsOnLowResolutionCrop()
    {
        var pano = new Slide(SlideKind.Panorama, new RectF(0, 0, 1, 0.5), 3);
        var tiny = new Slide(SlideKind.Crop, new RectF(0, 0, 0.1, 0.2));
        var fine = new Slide(SlideKind.Crop, new RectF(0, 0, 0.5, 0.5));

        var warnings = PostValidator.Validate(PostWith(pano, tiny, fine, Slide.FullImage), "", (6000, 4000));

        var low = Assert.Single(warnings, w => w.Kind == WarningKind.LowResolution);
        Assert.Equal(WarningKind.LowResolution, low.Kind);
        Assert.Equal(4, low.SlideNumber);
    }

    [Fact]
    public void Validate_CountsPanoramaPanelsTowardTheSlideLimit()
    {
        var pano = new Slide(SlideKind.Panorama, new RectF(0, 0, 1, 0.3), 5);
        var post = PostWith(pano, pano, pano, pano, Slide.FullImage);
        Assert.Equal(21, PostValidator.SlideCount(post));
        Assert.Contains(PostValidator.Validate(post, "", (60000, 4000)), w => w.Kind == WarningKind.TooManySlides);
    }

    [Fact]
    public void Validate_WarnsOnHashtagAndLengthLimits()
    {
        var tags = string.Join(' ', Enumerable.Range(1, 31).Select(i => $"#t{i}"));
        var longCaption = new string('x', 2201);
        var kinds = PostValidator.Validate(PostWith(Slide.FullImage), tags + "\n" + longCaption, (6000, 4000)).Select(w => w.Kind);
        Assert.Contains(WarningKind.TooManyHashtags, kinds);
        Assert.Contains(WarningKind.CaptionTooLong, kinds);
    }

    [Fact]
    public void Validate_ReportsMissingSourceAndNoSlides()
    {
        var kinds = PostValidator.Validate(PostWith(), "", null).Select(w => w.Kind).ToList();
        Assert.Contains(WarningKind.SourceMissing, kinds);
        Assert.Contains(WarningKind.NoSlides, kinds);
    }

    [Fact]
    public void Validate_WarnsWhenFullSlideIsUpscaled()
    {
        var warning = Assert.Single(PostValidator.Validate(PostWith(Slide.FullImage), "", (800, 600)));
        Assert.Equal(WarningKind.LowResolution, warning.Kind);
        Assert.Equal(1, warning.SlideNumber);
    }

    [Fact]
    public void Validate_WarnsWhenStoredFrameDoesNotFitTheImage()
    {
        // 4:5 crop fitted to a 6000x4000 image, then the source was relinked to a 6000x3000 re-stack.
        var crop = new Slide(SlideKind.Crop, SlideGeometry.DefaultCrop(AspectRatio.Portrait4x5, 6000, 4000));
        var offEdge = new Slide(SlideKind.Crop, new RectF(0.9, 0.1, 0.3, 0.4));

        var kinds = PostValidator.Validate(PostWith(crop, offEdge), "", (6000, 3000));

        Assert.Equal(2, kinds.Count(w => w.Kind == WarningKind.FrameAdjusted));
        Assert.Empty(PostValidator.Validate(PostWith(crop), "", (6000, 4000)).Where(w => w.Kind == WarningKind.FrameAdjusted));
    }
}
