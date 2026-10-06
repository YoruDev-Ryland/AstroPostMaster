using System.Runtime.InteropServices;
using AstroPostMaster.App.Services;
using AstroPostMaster.Imaging;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace AstroPostMaster.App.Views;

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

/// <summary>Decodes a ≤4096 px preview off the UI thread, converts it to sRGB and wraps it in a WriteableBitmap.</summary>
internal sealed class AvaloniaPreviewLoader : IPreviewLoader
{
    public Task<PreviewResult> LoadAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var loaded = ImageLoader.LoadPreview(path);
        cancellationToken.ThrowIfCancellationRequested();
        var (w, h) = (loaded.Bitmap.Width, loaded.Bitmap.Height);
        using var srgb = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(srgb))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(loaded.Bitmap, 0, 0);
        }

        var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            var rowBytes = w * 4;
            var source = srgb.GetPixels();
            for (var y = 0; y < h; y++)
                unsafe
                {
                    Buffer.MemoryCopy((byte*)source + (long)y * srgb.RowBytes, (byte*)frame.Address + (long)y * frame.RowBytes, frame.RowBytes, rowBytes);
                }
        }
        return new PreviewResult(bitmap, loaded.SourceWidth, loaded.SourceHeight);
    }, cancellationToken);
}
