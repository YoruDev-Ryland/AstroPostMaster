using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Slides;

/// <summary>
/// Frame math. Rects are fractions of the source image, so a frame whose pixel ratio (w/h) is r has
/// fractional W/H = r · imageHeight / imageWidth. A slide's angle rotates its rect about the centre in source
/// pixels; a rotated frame fits the image exactly when its axis-aligned bounding box does.
/// </summary>
public static class SlideGeometry
{
    public const int MinFramePixels = 16;

    /// <summary>Upper bound on either side of a full-resolution slide (memory and JPEG sanity).</summary>
    public const int MaxLongEdge = 8192;

    public static double FrameRatio(AspectRatio aspect, int panels) => FrameRatio(AspectSizes.Ratio(aspect), panels);

    public static double FrameRatio(double slideRatio, int panels) => slideRatio * Math.Max(1, panels);

    /// <summary>Largest pixel size with width/height = <paramref name="ratio"/> that fits inside the image.</summary>
    public static (double W, double H) MaxPixelSize(double ratio, int imageWidth, int imageHeight) =>
        (double)imageWidth / imageHeight > ratio
            ? (imageHeight * ratio, imageHeight)
            : (imageWidth, imageWidth / ratio);

    public static RectF DefaultCrop(AspectRatio aspect, int imageWidth, int imageHeight) =>
        DefaultCrop(AspectSizes.Ratio(aspect), imageWidth, imageHeight);

    public static RectF DefaultCrop(double slideRatio, int imageWidth, int imageHeight) =>
        Centered(FrameRatio(slideRatio, 1), 0.5, imageWidth, imageHeight);

    public static RectF DefaultPanorama(AspectRatio aspect, int panels, int imageWidth, int imageHeight) =>
        DefaultPanorama(AspectSizes.Ratio(aspect), panels, imageWidth, imageHeight);

    public static RectF DefaultPanorama(double slideRatio, int panels, int imageWidth, int imageHeight) =>
        Centered(FrameRatio(slideRatio, panels), 1.0, imageWidth, imageHeight);

    /// <summary>
    /// Forces <paramref name="rect"/> to <paramref name="ratio"/>, keeping its width and centre where possible,
    /// shrinking it to fit, then moving it fully inside the image.
    /// </summary>
    public static RectF FitLocked(RectF rect, double ratio, int imageWidth, int imageHeight) =>
        Fit(rect, ratio, imageWidth, imageHeight, 0);

    /// <summary>
    /// Like <see cref="FitLocked(RectF,double,int,int)"/> for a frame rotated by <paramref name="angle"/> degrees.
    /// A null <paramref name="ratio"/> keeps the rect's own shape (free frames).
    /// </summary>
    public static RectF Fit(RectF rect, double? ratio, int imageWidth, int imageHeight, double angle)
    {
        var r = ratio ?? PixelRatio(rect, imageWidth, imageHeight);
        var (c, s) = CosSin(angle);
        // Largest width whose rotated bounding box fits: w·c + h·s ≤ imageWidth and w·s + h·c ≤ imageHeight.
        var maxW = Math.Min(imageWidth / (c + s / r), imageHeight / (s + c / r));
        var w = Math.Clamp(rect.W * imageWidth, Math.Min(MinFramePixels, maxW), maxW);
        var h = w / r;
        var (ex, ey) = HalfExtents(w, h, c, s);
        var cx = Math.Clamp(rect.CenterX * imageWidth, Math.Min(ex, imageWidth / 2.0), Math.Max(imageWidth - ex, imageWidth / 2.0));
        var cy = Math.Clamp(rect.CenterY * imageHeight, Math.Min(ey, imageHeight / 2.0), Math.Max(imageHeight - ey, imageHeight / 2.0));
        return ToFraction(cx - w / 2, cy - h / 2, w, h, imageWidth, imageHeight);
    }

    /// <summary>Fits a stored slide to the post's shape (fixed or free) and the image.</summary>
    public static RectF FitSlide(Post post, Slide slide, int imageWidth, int imageHeight) =>
        Fit(slide.Rect, PostShape.FixedFrameRatio(post, slide), imageWidth, imageHeight, slide.Angle);

    /// <summary>True when the frame (centre and size in source pixels, rotated) lies inside the image.</summary>
    public static bool Inside(double cx, double cy, double w, double h, double angle, int imageWidth, int imageHeight)
    {
        var (c, s) = CosSin(angle);
        var (ex, ey) = HalfExtents(w, h, c, s);
        const double eps = 1e-6;
        return cx - ex >= -eps && cy - ey >= -eps && cx + ex <= imageWidth + eps && cy + ey <= imageHeight + eps;
    }

    /// <summary>The frame's corners in source pixels: top-left, top-right, bottom-right, bottom-left (before rotation).</summary>
    public static (double X, double Y)[] Corners(RectF rect, double angle, int imageWidth, int imageHeight)
    {
        var (cx, cy) = (rect.CenterX * imageWidth, rect.CenterY * imageHeight);
        var (hw, hh) = (rect.W * imageWidth / 2, rect.H * imageHeight / 2);
        var rad = angle * Math.PI / 180;
        var (c, s) = (Math.Cos(rad), Math.Sin(rad));
        return [Rotate(-hw, -hh), Rotate(hw, -hh), Rotate(hw, hh), Rotate(-hw, hh)];
        (double, double) Rotate(double x, double y) => (cx + x * c - y * s, cy + x * s + y * c);
    }

    /// <summary>Normalises degrees to (-180, 180].</summary>
    public static double NormalizeAngle(double angle)
    {
        if (!double.IsFinite(angle)) return 0;
        var a = angle % 360;
        if (a <= -180) a += 360;
        if (a > 180) a -= 360;
        return Math.Abs(a) < 1e-9 ? 0 : a;
    }

    public static double PixelRatio(RectF rect, int imageWidth, int imageHeight) =>
        rect.W > 0 && rect.H > 0 ? rect.W * imageWidth / (rect.H * imageHeight) : PostShape.DefaultFreeRatio;

    private static (double C, double S) CosSin(double angle)
    {
        var rad = angle * Math.PI / 180;
        return (Math.Abs(Math.Cos(rad)), Math.Abs(Math.Sin(rad)));
    }

    private static (double X, double Y) HalfExtents(double w, double h, double c, double s) => ((w * c + h * s) / 2, (w * s + h * c) / 2);

    public static IReadOnlyList<RectF> SplitPanorama(RectF band, int panels)
    {
        var w = band.W / panels;
        return Enumerable.Range(0, panels).Select(i => new RectF(band.X + i * w, band.Y, w, band.H)).ToList();
    }

    /// <summary>
    /// The panels of a band rotated by <paramref name="angle"/>: each is a frame of the same angle whose centre sits
    /// along the band's rotated axis. With no rotation this equals <see cref="SplitPanorama"/>.
    /// </summary>
    public static IReadOnlyList<RectF> SplitPanorama(RectF band, int panels, double angle, int imageWidth, int imageHeight)
    {
        if (angle == 0) return SplitPanorama(band, panels);
        var rad = angle * Math.PI / 180;
        var (c, s) = (Math.Cos(rad), Math.Sin(rad));
        var panelWidth = band.W * imageWidth / panels;
        var (cx, cy) = (band.CenterX * imageWidth, band.CenterY * imageHeight);
        return Enumerable.Range(0, panels).Select(i =>
        {
            var offset = (i + 0.5) * panelWidth - band.W * imageWidth / 2;
            var (px, py) = (cx + offset * c, cy + offset * s);
            return new RectF(px / imageWidth - band.W / panels / 2, py / imageHeight - band.H / 2, band.W / panels, band.H);
        }).ToList();
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
    public static (int W, int H) OutputSize(Slide slide, AspectRatio aspect, ExportSize size, int imageWidth, int imageHeight) =>
        size != ExportSize.Full ? FixedSize(aspect, size) : OutputSize(slide, AspectSizes.Ratio(aspect), size, imageWidth, imageHeight);

    /// <param name="ratio">One exported slide's width ÷ height (see <see cref="PostShape.SlideRatio"/>).</param>
    public static (int W, int H) OutputSize(Slide slide, double ratio, ExportSize size, int imageWidth, int imageHeight)
    {
        if (size != ExportSize.Full) return FixedSize(ratio, size);

        double w, h;
        if (slide.Kind == SlideKind.Full)
        {
            // The frame that encloses the whole image at this shape.
            if ((double)imageWidth / imageHeight > ratio) { w = imageWidth; h = imageWidth / ratio; }
            else { h = imageHeight; w = imageHeight * ratio; }
        }
        else
        {
            var fitted = Fit(slide.Rect, FrameRatio(ratio, slide.Panels), imageWidth, imageHeight, slide.Angle);
            w = fitted.W * imageWidth / Math.Max(1, slide.Panels);
            h = w / ratio;
        }

        if (Math.Max(w, h) > MaxLongEdge)
            return w >= h ? (MaxLongEdge, Round(MaxLongEdge / ratio)) : (Round(MaxLongEdge * ratio), MaxLongEdge);
        var ow = Round(w);
        return (ow, Round(ow / ratio));
    }

    /// <summary>The fixed output size for a non-Full export size.</summary>
    public static (int W, int H) FixedSize(AspectRatio aspect, ExportSize size) =>
        size == ExportSize.Instagram1080 ? AspectSizes.Output(aspect) : FixedSize(AspectSizes.Ratio(aspect), size);

    /// <summary>The fixed output size of a slide <paramref name="ratio"/> wide per unit of height. Instagram's is 1080 px wide.</summary>
    public static (int W, int H) FixedSize(double ratio, ExportSize size) => size switch
    {
        ExportSize.Instagram1080 => (1080, Round(1080 / ratio)),
        ExportSize.LongEdge4096 => LongEdge(ratio, 4096),
        ExportSize.LongEdge2048 => LongEdge(ratio, 2048),
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Full has no fixed size"),
    };

    /// <summary>Smallest decode scale (0,1] at which every slide still has at least its output resolution.</summary>
    public static double RequiredScale(IEnumerable<Slide> slides, AspectRatio aspect, int imageWidth, int imageHeight,
        ExportSize size = ExportSize.Instagram1080)
    {
        var fixedSize = size == ExportSize.Full ? default : FixedSize(aspect, size);
        return RequiredScale(slides, _ => fixedSize, imageWidth, imageHeight, size);
    }

    public static double RequiredScale(Post post, int imageWidth, int imageHeight, ExportSize size) =>
        RequiredScale(post.Slides, s => size == ExportSize.Full ? default
            : FixedSize(PostShape.SlideRatio(post, s, imageWidth, imageHeight), size), imageWidth, imageHeight, size);

    private static double RequiredScale(IEnumerable<Slide> slides, Func<Slide, (int W, int H)> outputSize, int imageWidth, int imageHeight,
        ExportSize size)
    {
        if (size == ExportSize.Full) return 1.0;
        var scale = 0.0;
        foreach (var slide in slides)
        {
            var (ow, oh) = outputSize(slide);
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
