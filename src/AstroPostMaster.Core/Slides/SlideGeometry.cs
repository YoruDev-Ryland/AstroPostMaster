using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Slides;

/// <summary>
/// Frame math. Rects are fractions of the source image, so a frame whose pixel ratio (w/h) is r has
/// fractional W/H = r · imageHeight / imageWidth. All public results lie inside [0,1].
/// </summary>
public static class SlideGeometry
{
    public const int MinFramePixels = 16;

    /// <summary>Upper bound on either side of a full-resolution slide (memory and JPEG sanity).</summary>
    public const int MaxLongEdge = 8192;

    public static double FrameRatio(AspectRatio aspect, int panels) => AspectSizes.Ratio(aspect) * Math.Max(1, panels);

    /// <summary>Largest pixel size with width/height = <paramref name="ratio"/> that fits inside the image.</summary>
    public static (double W, double H) MaxPixelSize(double ratio, int imageWidth, int imageHeight) =>
        (double)imageWidth / imageHeight > ratio
            ? (imageHeight * ratio, imageHeight)
            : (imageWidth, imageWidth / ratio);

    public static RectF DefaultCrop(AspectRatio aspect, int imageWidth, int imageHeight) =>
        Centered(FrameRatio(aspect, 1), 0.5, imageWidth, imageHeight);

    public static RectF DefaultPanorama(AspectRatio aspect, int panels, int imageWidth, int imageHeight) =>
        Centered(FrameRatio(aspect, panels), 1.0, imageWidth, imageHeight);

    /// <summary>
    /// Forces <paramref name="rect"/> to <paramref name="ratio"/>, keeping its width and centre where possible,
    /// shrinking it to fit, then moving it fully inside the image.
    /// </summary>
    public static RectF FitLocked(RectF rect, double ratio, int imageWidth, int imageHeight)
    {
        var (maxW, maxH) = MaxPixelSize(ratio, imageWidth, imageHeight);
        var w = Math.Clamp(rect.W * imageWidth, Math.Min(MinFramePixels, maxW), maxW);
        var h = w / ratio;
        if (h > maxH) { h = maxH; w = h * ratio; }
        var x = Math.Clamp(rect.CenterX * imageWidth - w / 2, 0, Math.Max(0, imageWidth - w));
        var y = Math.Clamp(rect.CenterY * imageHeight - h / 2, 0, Math.Max(0, imageHeight - h));
        return ToFraction(x, y, w, h, imageWidth, imageHeight);
    }

    public static IReadOnlyList<RectF> SplitPanorama(RectF band, int panels)
    {
        var w = band.W / panels;
        return Enumerable.Range(0, panels).Select(i => new RectF(band.X + i * w, band.Y, w, band.H)).ToList();
    }

    /// <summary>Rounds to whole pixels and clamps so the region is non-empty and inside the image.</summary>
    public static (int X, int Y, int W, int H) ToPixels(RectF rect, int imageWidth, int imageHeight)
    {
        var x = Math.Clamp((int)Math.Round(rect.X * imageWidth), 0, imageWidth - 1);
        var y = Math.Clamp((int)Math.Round(rect.Y * imageHeight), 0, imageHeight - 1);
        var w = Math.Clamp((int)Math.Round(rect.W * imageWidth), 1, imageWidth - x);
        var h = Math.Clamp((int)Math.Round(rect.H * imageHeight), 1, imageHeight - y);
        return (x, y, w, h);
    }

    /// <summary>The source scaled to fit inside the destination and centred (letterbox/pillarbox).</summary>
    public static (double X, double Y, double W, double H) FitInside(int sourceWidth, int sourceHeight, int destWidth, int destHeight)
    {
        var scale = Math.Min((double)destWidth / sourceWidth, (double)destHeight / sourceHeight);
        var w = sourceWidth * scale;
        var h = sourceHeight * scale;
        return ((destWidth - w) / 2, (destHeight - h) / 2, w, h);
    }

    /// <summary>Source pixels spanning one output slide's width (crop and panorama slides).</summary>
    public static double SourcePixelsAcross(Slide slide, int imageWidth) =>
        slide.Rect.W * imageWidth / Math.Max(1, slide.Panels);

    /// <summary>Pixel size of one exported slide (one panel of a panorama).</summary>
    public static (int W, int H) OutputSize(Slide slide, AspectRatio aspect, ExportSize size, int imageWidth, int imageHeight)
    {
        if (size != ExportSize.Full) return FixedSize(aspect, size);

        var ratio = AspectSizes.Ratio(aspect);
        double w, h;
        if (slide.Kind == SlideKind.Full)
        {
            // The frame that encloses the whole image at this shape.
            if ((double)imageWidth / imageHeight > ratio) { w = imageWidth; h = imageWidth / ratio; }
            else { h = imageHeight; w = imageHeight * ratio; }
        }
        else
        {
            var fitted = FitLocked(slide.Rect, FrameRatio(aspect, slide.Panels), imageWidth, imageHeight);
            w = fitted.W * imageWidth / Math.Max(1, slide.Panels);
            h = w / ratio;
        }

        if (Math.Max(w, h) > MaxLongEdge)
            return w >= h ? (MaxLongEdge, Round(MaxLongEdge / ratio)) : (Round(MaxLongEdge * ratio), MaxLongEdge);
        var ow = Round(w);
        return (ow, Round(ow / ratio));
    }

    /// <summary>The fixed output size for a non-Full export size.</summary>
    public static (int W, int H) FixedSize(AspectRatio aspect, ExportSize size)
    {
        var ratio = AspectSizes.Ratio(aspect);
        return size switch
        {
            ExportSize.Instagram1080 => AspectSizes.Output(aspect),
            ExportSize.LongEdge4096 => LongEdge(ratio, 4096),
            ExportSize.LongEdge2048 => LongEdge(ratio, 2048),
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Full has no fixed size"),
        };
    }

    /// <summary>Smallest decode scale (0,1] at which every slide still has at least its output resolution.</summary>
    public static double RequiredScale(IEnumerable<Slide> slides, AspectRatio aspect, int imageWidth, int imageHeight,
        ExportSize size = ExportSize.Instagram1080)
    {
        if (size == ExportSize.Full) return 1.0;
        var (ow, oh) = FixedSize(aspect, size);
        var scale = 0.0;
        foreach (var slide in slides)
        {
            var needed = slide.Kind == SlideKind.Full
                ? Math.Min((double)ow / imageWidth, (double)oh / imageHeight)
                : ow / SourcePixelsAcross(slide, imageWidth);
            scale = Math.Max(scale, needed);
        }
        return scale <= 0 ? 1.0 : Math.Min(1.0, scale);
    }

    private static (int W, int H) LongEdge(double ratio, int edge) =>
        ratio >= 1 ? (edge, Round(edge / ratio)) : (Round(edge * ratio), edge);

    private static int Round(double v) => Math.Max(1, (int)Math.Round(v, MidpointRounding.AwayFromZero));

    private static RectF Centered(double ratio, double scale, int imageWidth, int imageHeight)
    {
        var (maxW, maxH) = MaxPixelSize(ratio, imageWidth, imageHeight);
        var (w, h) = (maxW * scale, maxH * scale);
        return ToFraction((imageWidth - w) / 2, (imageHeight - h) / 2, w, h, imageWidth, imageHeight);
    }

    private static RectF ToFraction(double x, double y, double w, double h, int imageWidth, int imageHeight) =>
        new(x / imageWidth, y / imageHeight, w / imageWidth, h / imageHeight);
}
