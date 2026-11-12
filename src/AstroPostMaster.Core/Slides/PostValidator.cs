using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Slides;

public enum WarningKind { SourceMissing, NoSlides, TooManySlides, TooManyHashtags, CaptionTooLong, LowResolution, FrameAdjusted, ShapeOutsideInstagram, MixedShapes }

/// <param name="SlideNumber">1-based carousel position (panoramas occupy several), when the warning is about one slide.</param>
public sealed record PostWarning(WarningKind Kind, string Message, int? SlideNumber = null);

/// <summary>Live warnings for the status bar. Warnings never block export.</summary>
public static class PostValidator
{
    public const int MaxSlides = 20;

    private const double FrameTolerance = 0.002;

    private static bool Close(RectF a, RectF b) =>
        Math.Abs(a.X - b.X) <= FrameTolerance && Math.Abs(a.Y - b.Y) <= FrameTolerance
        && Math.Abs(a.W - b.W) <= FrameTolerance && Math.Abs(a.H - b.H) <= FrameTolerance;

    public static int SlideCount(Post post) => post.Slides.Sum(s => s.Kind == SlideKind.Panorama ? Math.Max(1, s.Panels) : 1);

    /// <param name="sourceSize">Pixel size of the source image, or null if it is missing or unreadable.</param>
    /// <param name="exportSize">At <see cref="ExportSize.Full"/> nothing is upscaled, so no low-resolution warnings.</param>
    public static IReadOnlyList<PostWarning> Validate(Post post, string caption, (int Width, int Height)? sourceSize,
        ExportSize exportSize = ExportSize.Instagram1080)
    {
        var warnings = new List<PostWarning>();

        if (post.SourcePath is not null && sourceSize is null)
            warnings.Add(new(WarningKind.SourceMissing, $"Source image not found: {post.SourcePath}"));

        var count = SlideCount(post);
        if (count == 0)
            warnings.Add(new(WarningKind.NoSlides, "Add at least one slide."));
        if (count > MaxSlides)
            warnings.Add(new(WarningKind.TooManySlides, $"{count} slides; Instagram allows {MaxSlides}."));

        var tags = CaptionStats.HashtagCount(caption);
        if (tags > Hashtags.InstagramLimit)
            warnings.Add(new(WarningKind.TooManyHashtags, $"{tags} hashtags; Instagram allows {Hashtags.InstagramLimit}."));

        var chars = CaptionStats.CharacterCount(caption);
        if (chars > CaptionStats.MaxCharacters)
            warnings.Add(new(WarningKind.CaptionTooLong, $"{chars} characters; Instagram allows {CaptionStats.MaxCharacters}."));

        if (PostShape.FixedRatio(post) is { } fixedRatio && PostShape.OutsideInstagram(fixedRatio))
            warnings.Add(new(WarningKind.ShapeOutsideInstagram, $"Instagram allows 4:5 to 1.91:1 and will crop {PostShape.Label(post)} slides."));

        if (sourceSize is { } size)
        {
            if (post.Aspect == AspectRatio.Free)
            {
                var ratios = post.Slides.Select(s => PostShape.SlideRatio(post, s, size.Width, size.Height)).ToList();
                if (ratios.Count > 1 && ratios.Any(r => Math.Abs(r / ratios[0] - 1) > 0.01))
                    warnings.Add(new(WarningKind.MixedShapes, "Slides have different shapes; Instagram crops them all to the first slide's shape."));
                if (ratios.Count > 0 && PostShape.OutsideInstagram(ratios[0]))
                    warnings.Add(new(WarningKind.ShapeOutsideInstagram, "Instagram allows 4:5 to 1.91:1 and will crop the first slide's shape."));
            }

            var number = 1;
            foreach (var slide in post.Slides)
            {
                var ratio = PostShape.SlideRatio(post, slide, size.Width, size.Height);
                var (outputWidth, outputHeight) = exportSize == ExportSize.Full ? (0, 0)
                    : post.Aspect is AspectRatio.Custom or AspectRatio.Free
                        ? SlideGeometry.FixedSize(ratio, exportSize)
                        : SlideGeometry.FixedSize(post.Aspect, exportSize);
                if (slide.Kind == SlideKind.Full)
                {
                    if (Math.Min((double)outputWidth / size.Width, (double)outputHeight / size.Height) > 1)
                        warnings.Add(new(WarningKind.LowResolution,
                            $"Slide {number}: the image is smaller than the slide and will be upscaled.", number));
                }
                else
                {
                    var fitted = SlideGeometry.FitSlide(post, slide, size.Width, size.Height);
                    if (!Close(fitted, slide.Rect))
                        warnings.Add(new(WarningKind.FrameAdjusted,
                            $"Slide {number}'s frame doesn't fit this image and will be adjusted on export.", number));

                    var across = SlideGeometry.SourcePixelsAcross(slide with { Rect = fitted }, size.Width);
                    if (across < outputWidth)
                        warnings.Add(new(WarningKind.LowResolution,
                            $"Slide {number} uses {across:0} source pixels across and will be upscaled to {outputWidth}.", number));
                }
                number += slide.Kind == SlideKind.Panorama ? Math.Max(1, slide.Panels) : 1;
            }
        }

        return warnings;
    }
}
