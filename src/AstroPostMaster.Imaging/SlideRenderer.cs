using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using SkiaSharp;

namespace AstroPostMaster.Imaging;

public static class SlideRenderer
{
    private static readonly SKSamplingOptions Cubic = new(SKCubicResampler.Mitchell);
    private static readonly SKSamplingOptions Halving = new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>
    /// Renders one slide into sRGB bitmaps of the output size: one per panel (panoramas are rendered as one
    /// strip and sliced, so neighbouring panels line up exactly). Transparent areas and padding are black.
    /// The caller disposes the returned bitmaps.
    /// </summary>
    public static IReadOnlyList<SKBitmap> Render(LoadedImage source, Slide slide, AspectRatio aspect, WatermarkSettings? watermark = null,
        ExportSize size = ExportSize.Instagram1080) =>
        Render(source, slide, AspectSizes.Ratio(aspect), watermark, size,
            size == ExportSize.Full ? null : SlideGeometry.FixedSize(aspect, size));

    /// <summary>Renders a slide of <paramref name="post"/>, whose shape may be custom or free.</summary>
    public static IReadOnlyList<SKBitmap> Render(LoadedImage source, Post post, Slide slide, WatermarkSettings? watermark = null,
        ExportSize size = ExportSize.Instagram1080) =>
        post.Aspect is AspectRatio.Custom or AspectRatio.Free
            ? Render(source, slide, PostShape.SlideRatio(post, slide, source.SourceWidth, source.SourceHeight), watermark, size, null)
            : Render(source, slide, post.Aspect, watermark, size);

    /// <param name="ratio">One output slide's width ÷ height.</param>
    /// <param name="fixedSize">Overrides the computed output size (preset shapes keep their exact Instagram sizes).</param>
    private static IReadOnlyList<SKBitmap> Render(LoadedImage source, Slide slide, double ratio, WatermarkSettings? watermark,
        ExportSize size, (int W, int H)? fixedSize)
    {
        var (outputWidth, outputHeight) = fixedSize ?? SlideGeometry.OutputSize(slide, ratio, size, source.SourceWidth, source.SourceHeight);
        var panels = slide.Kind == SlideKind.Panorama ? Math.Max(1, slide.Panels) : 1;
        var stripWidth = outputWidth * panels;
        var srgb = SKColorSpace.CreateSrgb();

        using var strip = new SKBitmap(new SKImageInfo(stripWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        var image = source.AsImage();
        using (var canvas = new SKCanvas(strip))
        {
            canvas.Clear(SKColors.Black);
            var (bw, bh) = (source.Bitmap.Width, source.Bitmap.Height);
            var angle = slide.Kind == SlideKind.Full ? 0 : SlideGeometry.NormalizeAngle(slide.Angle);
            if (angle != 0)
            {
                DrawRotated(canvas, image, slide, ratio * panels, angle, bw, bh, stripWidth, outputHeight);
            }
            else
            {
                SKRectI region;
                SKRect dest;
                if (slide.Kind == SlideKind.Full)
                {
                    region = new SKRectI(0, 0, bw, bh);
                    var (x, y, w, h) = SlideGeometry.FitInside(bw, bh, stripWidth, outputHeight);
                    dest = SKRect.Create((float)x, (float)y, (float)w, (float)h);
                }
                else
                {
                    // Re-fit stored frames: they may be stale (relinked source) or hand-edited off the image.
                    var fitted = SlideGeometry.FitLocked(slide.Rect, SlideGeometry.FrameRatio(ratio, panels), bw, bh);
                    var (x, y, w, h) = SlideGeometry.ToPixels(fitted, bw, bh);
                    region = SKRectI.Create(x, y, w, h);
                    dest = SKRect.Create(0, 0, stripWidth, outputHeight);
                }

                using var reduced = Reduce(image, region, (int)Math.Ceiling(dest.Width), (int)Math.Ceiling(dest.Height));
                canvas.DrawImage(reduced, SKRect.Create(0, 0, reduced.Width, reduced.Height), dest, Cubic);
            }
        }

        var slides = new List<SKBitmap>(panels);
        for (var i = 0; i < panels; i++)
        {
            var panel = new SKBitmap(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
            using var canvas = new SKCanvas(panel);
            canvas.DrawBitmap(strip, SKRect.Create(i * outputWidth, 0, outputWidth, outputHeight), SKRect.Create(0, 0, outputWidth, outputHeight));
            if (watermark is not null) DrawWatermark(canvas, outputWidth, outputHeight, watermark);
            slides.Add(panel);
        }
        return slides;
    }

    /// <summary>
    /// Draws a rotated frame into the whole strip: the frame's bounding box is cropped and reduced like an
    /// unrotated frame, then drawn turned back by the frame's angle so the frame fills the strip upright.
    /// </summary>
    private static void DrawRotated(SKCanvas canvas, SKImage image, Slide slide, double frameRatio, double angle,
        int bw, int bh, int stripWidth, int stripHeight)
    {
        var fitted = SlideGeometry.Fit(slide.Rect, frameRatio, bw, bh, angle);
        var corners = SlideGeometry.Corners(fitted, angle, bw, bh);
        var left = Math.Clamp((int)Math.Floor(corners.Min(p => p.X)), 0, bw - 1);
        var top = Math.Clamp((int)Math.Floor(corners.Min(p => p.Y)), 0, bh - 1);
        var right = Math.Clamp((int)Math.Ceiling(corners.Max(p => p.X)), left + 1, bw);
        var bottom = Math.Clamp((int)Math.Ceiling(corners.Max(p => p.Y)), top + 1, bh);
        var region = new SKRectI(left, top, right, bottom);

        var frameWidth = fitted.W * bw;
        var frameHeight = fitted.H * bh;
        var scale = stripWidth / frameWidth; // output pixels per source pixel
        using var reduced = Reduce(image, region,
            (int)Math.Ceiling(region.Width * scale), (int)Math.Ceiling(region.Height * scale));
        var reducedScale = (double)reduced.Width / region.Width; // reduced pixels per source pixel

        // Map reduced pixels → strip: into source pixels, about the frame centre, unrotate, then scale to the strip.
        canvas.Save();
        canvas.Translate(stripWidth / 2f, stripHeight / 2f);
        canvas.Scale((float)scale, (float)(stripHeight / frameHeight));
        canvas.RotateDegrees((float)-angle);
        canvas.Translate((float)(left - fitted.CenterX * bw), (float)(top - fitted.CenterY * bh));
        canvas.Scale((float)(1 / reducedScale));
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(reduced, SKRect.Create(0, 0, reduced.Width, reduced.Height), SKRect.Create(0, 0, reduced.Width, reduced.Height),
            Cubic, paint);
        canvas.Restore();
    }

    /// <summary>
    /// Crops <paramref name="region"/>, then halves it with a 2×2 box filter while it is still at least twice the
    /// target size, so the final cubic resample never skips pixels (keeps stars from aliasing).
    /// </summary>
    private static SKImage Reduce(SKImage image, SKRectI region, int targetWidth, int targetHeight)
    {
        var current = image.Subset(region) ?? throw new InvalidOperationException("Slide region lies outside the image.");
        while (current.Width >= targetWidth * 2 && current.Height >= targetHeight * 2)
        {
            var info = new SKImageInfo(current.Width / 2, current.Height / 2, SKColorType.Rgba8888, SKAlphaType.Premul, current.ColorSpace);
            using var surface = SKSurface.Create(info);
            surface.Canvas.DrawImage(current, SKRect.Create(info.Width, info.Height), Halving);
            var next = surface.Snapshot();
            current.Dispose();
            current = next;
        }
        return current;
    }

    private static void DrawWatermark(SKCanvas canvas, int width, int height, WatermarkSettings watermark)
    {
        if (!watermark.Enabled || string.IsNullOrWhiteSpace(watermark.Text)) return;
        var size = (float)(height * Math.Clamp(watermark.SizeFraction, 0.01, 0.1));
        using var font = new SKFont(SKTypeface.Default, size);
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(watermark.Opacity, 0, 1))),
            IsAntialias = true,
        };
        var textWidth = font.MeasureText(watermark.Text);
        var left = watermark.Corner is Corner.TopLeft or Corner.BottomLeft;
        var top = watermark.Corner is Corner.TopLeft or Corner.TopRight;
        var x = left ? size : width - size - textWidth;
        var y = top ? size * 2 : height - size;
        canvas.DrawText(watermark.Text, x, y, SKTextAlign.Left, font, paint);
    }
}
