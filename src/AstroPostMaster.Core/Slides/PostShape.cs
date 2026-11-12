using System.Globalization;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Slides;

/// <summary>A post's slide shape: a preset, a custom ratio, or free (each frame keeps its own shape).</summary>
public static class PostShape
{
    /// <summary>Custom ratios are kept within this range (and its inverse) so frames stay usable.</summary>
    public const double MaxCustomRatio = 10;

    /// <summary>Instagram's portrait and landscape limits (4:5 and 1080×566).</summary>
    public const double InstagramMinRatio = 4.0 / 5;
    public const double InstagramMaxRatio = 1080.0 / 566;

    /// <summary>Shape used for a free post's new frames when there is nothing to copy.</summary>
    public const double DefaultFreeRatio = 4.0 / 5;

    /// <summary>One slide's width ÷ height shared by every slide, or null when the post is free.</summary>
    public static double? FixedRatio(Post post) => post.Aspect switch
    {
        AspectRatio.Free => null,
        AspectRatio.Custom => CustomRatio(post.CustomAspectWidth, post.CustomAspectHeight),
        var preset => AspectSizes.Ratio(preset),
    };

    public static double CustomRatio(double width, double height) =>
        width > 0 && height > 0 && double.IsFinite(width / height)
            ? Math.Clamp(width / height, 1 / MaxCustomRatio, MaxCustomRatio)
            : DefaultFreeRatio;

    /// <summary>
    /// Width ÷ height of one exported slide (one panel of a panorama). Free posts use the frame's own shape;
    /// a free Full slide is the image's own shape.
    /// </summary>
    public static double SlideRatio(Post post, Slide slide, int imageWidth, int imageHeight) =>
        FixedRatio(post) ?? FreeSlideRatio(slide, imageWidth, imageHeight);

    public static double FreeSlideRatio(Slide slide, int imageWidth, int imageHeight)
    {
        if (slide.Kind == SlideKind.Full || slide.Rect.W <= 0 || slide.Rect.H <= 0) return (double)imageWidth / imageHeight;
        var panels = slide.Kind == SlideKind.Panorama ? Math.Max(1, slide.Panels) : 1;
        return slide.Rect.W * imageWidth / (slide.Rect.H * imageHeight) / panels;
    }

    /// <summary>Ratio for a whole frame (all panels) of <paramref name="slide"/>, or null when free.</summary>
    public static double? FixedFrameRatio(Post post, Slide slide) =>
        FixedRatio(post) is { } r ? r * (slide.Kind == SlideKind.Panorama ? Math.Max(1, slide.Panels) : 1) : null;

    public static bool OutsideInstagram(double ratio) => ratio < InstagramMinRatio - 1e-3 || ratio > InstagramMaxRatio + 1e-3;

    public static string Label(Post post) => post.Aspect switch
    {
        AspectRatio.Free => "Free",
        AspectRatio.Custom => CustomLabel(post.CustomAspectWidth, post.CustomAspectHeight),
        var preset => AspectSizes.Label(preset),
    };

    public static string CustomLabel(double width, double height) =>
        $"{width.ToString("0.##", CultureInfo.InvariantCulture)}:{height.ToString("0.##", CultureInfo.InvariantCulture)}";

    /// <summary>Parses "5:4", "5x4", "5/4", "5 4" or a single ratio like "1.25". Returns false for anything else.</summary>
    public static bool TryParse(string? text, out double width, out double height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split([':', 'x', 'X', '×', '/', ' '], StringSplitOptions.RemoveEmptyEntries);
        static bool Num(string s, out double v) =>
            double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v) && v > 0;
        switch (parts.Length)
        {
            case 1 when Num(parts[0], out var r):
                (width, height) = (r, 1);
                break;
            case 2 when Num(parts[0], out var w) && Num(parts[1], out var h):
                (width, height) = (w, h);
                break;
            default:
                return false;
        }
        var ratio = width / height;
        return ratio >= 1 / MaxCustomRatio && ratio <= MaxCustomRatio;
    }
}
