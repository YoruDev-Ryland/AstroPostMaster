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
        ExportSize size = ExportSize.Instagram1080)
    {
        var (outputWidth, outputHeight) = SlideGeometry.OutputSize(slide, aspect, size, source.SourceWidth, source.SourceHeight);
        var panels = slide.Kind == SlideKind.Panorama ? Math.Max(1, slide.Panels) : 1;
        var stripWidth = outputWidth * panels;
        var srgb = SKColorSpace.CreateSrgb();

        using var strip = new SKBitmap(new SKImageInfo(stripWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        var image = source.AsImage();
        using (var canvas = new SKCanvas(strip))
        {
            canvas.Clear(SKColors.Black);
            var (bw, bh) = (source.Bitmap.Width, source.Bitmap.Height);
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
                var fitted = SlideGeometry.FitLocked(slide.Rect, SlideGeometry.FrameRatio(aspect, panels), bw, bh);
                var (x, y, w, h) = SlideGeometry.ToPixels(fitted, bw, bh);
                region = SKRectI.Create(x, y, w, h);
                dest = SKRect.Create(0, 0, stripWidth, outputHeight);
            }

            using var reduced = Reduce(image, region, (int)Math.Ceiling(dest.Width), (int)Math.Ceiling(dest.Height));
            canvas.DrawImage(reduced, SKRect.Create(0, 0, reduced.Width, reduced.Height), dest, Cubic);
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
