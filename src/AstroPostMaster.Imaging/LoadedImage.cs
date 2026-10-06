using SkiaSharp;

namespace AstroPostMaster.Imaging;

/// <summary>A decoded image, possibly at reduced scale. SourceWidth/Height are the file's full-resolution (upright) size.</summary>
public sealed class LoadedImage(SKBitmap bitmap, int sourceWidth, int sourceHeight) : IDisposable
{
    private SKImage? _image;

    public SKBitmap Bitmap { get; } = bitmap;
    public int SourceWidth { get; } = sourceWidth;
    public int SourceHeight { get; } = sourceHeight;
    public double Scale => (double)Bitmap.Width / SourceWidth;

    /// <summary>
    /// An SKImage sharing the bitmap's pixels (no copy; the bitmap is marked immutable). Created once and owned
    /// by this instance, so rendering many slides never duplicates a huge source in memory.
    /// </summary>
    public SKImage AsImage()
    {
        if (_image is null)
        {
            Bitmap.SetImmutable();
            _image = SKImage.FromBitmap(Bitmap);
        }
        return _image;
    }

    public void Dispose()
    {
        _image?.Dispose();
        Bitmap.Dispose();
    }
}
