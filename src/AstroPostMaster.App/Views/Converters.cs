using System.Globalization;
using AstroPostMaster.Core.Model;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AstroPostMaster.App.Views;

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
