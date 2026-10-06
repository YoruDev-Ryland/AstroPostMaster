using System.Globalization;
using AstroPostMaster.Core.Model;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AstroPostMaster.App.Views;

/// <summary>[preview bitmap, slide rect, slide kind] → thumbnail image (cropped region, or the whole image for Full).</summary>
public sealed class ThumbnailConverter : IMultiValueConverter
{
    public static readonly ThumbnailConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3 || values[0] is not Bitmap bitmap || values[1] is not RectF rect || values[2] is not SlideKind kind)
            return null;
        if (kind == SlideKind.Full) return bitmap;
        var (w, h) = (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var x = Math.Clamp((int)Math.Round(rect.X * w), 0, w - 1);
        var y = Math.Clamp((int)Math.Round(rect.Y * h), 0, h - 1);
        var cw = Math.Clamp((int)Math.Round(rect.W * w), 1, w - x);
        var ch = Math.Clamp((int)Math.Round(rect.H * h), 1, h - y);
        return new CroppedBitmap(bitmap, new PixelRect(x, y, cw, ch));
    }
}

/// <summary>PNG bytes → Bitmap (for the QR code).</summary>
public sealed class PngBytesConverter : IValueConverter
{
    public static readonly PngBytesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is byte[] { Length: > 0 } bytes ? new Bitmap(new MemoryStream(bytes)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Preview object (typed as object in view models) → IImage.</summary>
public sealed class ImageConverter : IValueConverter
{
    public static readonly ImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value as IImage;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
