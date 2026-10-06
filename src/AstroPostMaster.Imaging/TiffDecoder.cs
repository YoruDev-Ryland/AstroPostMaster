using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;

namespace AstroPostMaster.Imaging;

/// <summary>TIFF via LibTiff.NET (Skia has no TIFF codec). Handles 8/16-bit, gray, RGB, alpha and embedded ICC.</summary>
internal static class TiffDecoder
{
    static TiffDecoder() => Tiff.SetErrorHandler(new QuietErrorHandler());

    public static bool IsTiff(string path)
    {
        Span<byte> h = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        if (stream.ReadAtLeast(h, 4, throwOnEndOfStream: false) < 4) return false;
        return (h[0] == 'I' && h[1] == 'I' && (h[2] == 42 || h[2] == 43) && h[3] == 0)
            || (h[0] == 'M' && h[1] == 'M' && h[2] == 0 && (h[3] == 42 || h[3] == 43));
    }

    public static (int Width, int Height) ReadSize(string path)
    {
        using var tif = Open(path);
        return (tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt(), tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt());
    }

    /// <summary>
    /// Decodes into a pinned raster that the bitmap uses directly (no second copy). LibTiff's RGBA interface returns
    /// premultiplied pixels, so the bitmap is tagged Premul. When <paramref name="minScale"/> &lt; 1 the result is
    /// downscaled and the full-size raster released.
    /// </summary>
    public static LoadedImage Decode(string path, double minScale)
    {
        using var tif = Open(path);
        var width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
        var height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

        // ReadRGBAImage packs each pixel as 0xAABBGGRR, i.e. bytes R,G,B,A in memory on little-endian machines.
        var raster = new int[checked(width * height)];
        if (!tif.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
            throw new ImageLoadException(path, "unsupported TIFF layout");

        var icc = tif.GetField(TiffTag.ICCPROFILE);
        var colorSpace = icc is { Length: >= 2 } ? SKColorSpace.CreateIcc(icc[1].GetBytes()) : null;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace ?? SKColorSpace.CreateSrgb());

        var handle = GCHandle.Alloc(raster, GCHandleType.Pinned);
        var full = new SKBitmap();
        if (!full.InstallPixels(info, handle.AddrOfPinnedObject(), width * 4, (_, context) => ((GCHandle)context!).Free(), handle))
        {
            handle.Free();
            full.Dispose();
            throw new ImageLoadException(path, "could not allocate image memory");
        }

        if (minScale >= 1) return new LoadedImage(full, width, height);

        var scaledInfo = info.WithSize(Math.Max(1, (int)Math.Ceiling(width * minScale)), Math.Max(1, (int)Math.Ceiling(height * minScale)));
        var scaled = full.Resize(scaledInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        full.Dispose();
        return new LoadedImage(scaled ?? throw new ImageLoadException(path, "could not downscale TIFF"), width, height);
    }

    private static Tiff Open(string path) =>
        Tiff.Open(path, "r") ?? throw new ImageLoadException(path, "not a readable TIFF");

    private sealed class QuietErrorHandler : TiffErrorHandler
    {
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args) { }
        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args) { }
        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
    }
}
