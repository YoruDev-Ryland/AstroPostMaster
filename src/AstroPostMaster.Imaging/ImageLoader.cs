using SkiaSharp;

namespace AstroPostMaster.Imaging;

public static class ImageLoader
{
    public static (int Width, int Height) ReadSize(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Source image not found.", path);
        if (TiffDecoder.IsTiff(path)) return TiffDecoder.ReadSize(path);
        using var stream = OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new ImageLoadException(path, "unsupported or corrupt image");
        return SwapsAxes(codec.EncodedOrigin) ? (codec.Info.Height, codec.Info.Width) : (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>
    /// Decodes to RGBA8888 in the file's own color space (sRGB when untagged). JPEGs decode at the smallest
    /// libjpeg scale (n/8) that is still at least <paramref name="minScale"/>; other formats decode at full size.
    /// </summary>
    public static LoadedImage Load(string path, double minScale = 1.0)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Source image not found.", path);
        minScale = Math.Clamp(minScale, 1e-3, 1.0);
        return TiffDecoder.IsTiff(path) ? TiffDecoder.Decode(path, minScale) : LoadWithSkia(path, minScale);
    }

    /// <summary>A copy no larger than <paramref name="maxLongEdge"/> on its long side, for the editor canvas.</summary>
    public static LoadedImage LoadPreview(string path, int maxLongEdge = 4096)
    {
        var (width, height) = ReadSize(path);
        var loaded = Load(path, Math.Min(1.0, (double)maxLongEdge / Math.Max(width, height)));
        var longEdge = Math.Max(loaded.Bitmap.Width, loaded.Bitmap.Height);
        if (longEdge <= maxLongEdge) return loaded;

        var factor = (double)maxLongEdge / longEdge;
        var info = loaded.Bitmap.Info.WithSize(
            Math.Max(1, (int)Math.Round(loaded.Bitmap.Width * factor)),
            Math.Max(1, (int)Math.Round(loaded.Bitmap.Height * factor)));
        var resized = loaded.Bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new ImageLoadException(path, "could not create preview");
        loaded.Dispose();
        return new LoadedImage(resized, width, height);
    }

    private static LoadedImage LoadWithSkia(string path, double minScale)
    {
        using var stream = OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new ImageLoadException(path, "unsupported or corrupt image");
        var full = codec.Info;
        var size = codec.GetScaledDimensions((float)minScale);
        if (size.Width < Math.Ceiling(full.Width * minScale) || size.Height < Math.Ceiling(full.Height * minScale))
            size = full.Size;

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul,
            full.ColorSpace ?? SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
        {
            bitmap.Dispose();
            throw new ImageLoadException(path, $"decoder reported {result}");
        }
        var origin = codec.EncodedOrigin;
        if (origin == SKEncodedOrigin.TopLeft) return new LoadedImage(bitmap, full.Width, full.Height);

        var upright = Orient(bitmap, origin);
        bitmap.Dispose();
        return SwapsAxes(origin)
            ? new LoadedImage(upright, full.Height, full.Width)
            : new LoadedImage(upright, full.Width, full.Height);
    }

    private static bool SwapsAxes(SKEncodedOrigin origin) => origin is
        SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>Applies an EXIF orientation so the bitmap is upright.</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var (w, h) = SwapsAxes(origin) ? (source.Height, source.Width) : (source.Width, source.Height);
        // SKMatrix(scaleX, skewX, transX, skewY, scaleY, transY, persp0, persp1, persp2): x' = sx·x + kx·y + tx, y' = ky·x + sy·y + ty.
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        var result = new SKBitmap(source.Info.WithSize(w, h));
        using var canvas = new SKCanvas(result);
        canvas.SetMatrix(m);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }

    // Opening through .NET (not Skia's path API) keeps non-ASCII paths working on Windows.
    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
}
