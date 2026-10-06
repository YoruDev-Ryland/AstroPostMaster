using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;

namespace AstroPostMaster.Imaging.Tests;

/// <summary>Generates small test images on disk, so the repo carries no binary fixtures.</summary>
internal static class Fixtures
{
    public static SKColorSpace AdobeRgb => SKColorSpace.CreateRgb(SKColorSpaceTransferFn.TwoDotTwo, SKColorSpaceXyz.AdobeRgb);

    public static void WriteRgb(string path, int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel,
        SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg, SKColorSpace? colorSpace = null)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = (y * width + x) * 4;
                bytes[i] = r; bytes[i + 1] = g; bytes[i + 2] = b; bytes[i + 3] = 255;
            }
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace ?? SKColorSpace.CreateSrgb());
        Save(path, info, bytes, format);
    }

    public static void WriteGray(string path, int width, int height, byte value)
    {
        var bytes = Enumerable.Repeat(value, width * height).ToArray();
        Save(path, new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque), bytes, SKEncodedImageFormat.Png);
    }

    /// <summary>Left half fully transparent, right half opaque (200, 50, 50).</summary>
    public static void WriteHalfTransparent(string path, int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = width / 2; x < width; x++)
            {
                var i = (y * width + x) * 4;
                bytes[i] = 200; bytes[i + 1] = 50; bytes[i + 2] = 50; bytes[i + 3] = 255;
            }
        Save(path, new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), bytes, SKEncodedImageFormat.Png);
    }

    public static void WriteTiff16(string path, int width, int height, ushort r, ushort g, ushort b)
    {
        using var tif = Tiff.Open(path, "w");
        tif.SetField(TiffTag.IMAGEWIDTH, width);
        tif.SetField(TiffTag.IMAGELENGTH, height);
        tif.SetField(TiffTag.SAMPLESPERPIXEL, 3);
        tif.SetField(TiffTag.BITSPERSAMPLE, 16);
        tif.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tif.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
        tif.SetField(TiffTag.ROWSPERSTRIP, height);
        tif.SetField(TiffTag.COMPRESSION, Compression.NONE);
        var row = new byte[width * 6];
        for (var x = 0; x < width; x++)
        {
            BitConverter.TryWriteBytes(row.AsSpan(x * 6), r);
            BitConverter.TryWriteBytes(row.AsSpan(x * 6 + 2), g);
            BitConverter.TryWriteBytes(row.AsSpan(x * 6 + 4), b);
        }
        for (var y = 0; y < height; y++) tif.WriteScanline(row, y);
    }

    private static void Save(string path, SKImageInfo info, byte[] bytes, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        using var data = bitmap.Encode(format, 95);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>8-bit RGBA TIFF with unassociated (straight) alpha.</summary>
    public static void WriteTiffRgba8(string path, int width, int height, byte r, byte g, byte b, byte a)
    {
        using var tif = Tiff.Open(path, "w");
        tif.SetField(TiffTag.IMAGEWIDTH, width);
        tif.SetField(TiffTag.IMAGELENGTH, height);
        tif.SetField(TiffTag.SAMPLESPERPIXEL, 4);
        tif.SetField(TiffTag.BITSPERSAMPLE, 8);
        tif.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)ExtraSample.UNASSALPHA });
        tif.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tif.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
        tif.SetField(TiffTag.ROWSPERSTRIP, height);
        tif.SetField(TiffTag.COMPRESSION, Compression.NONE);
        var row = new byte[width * 4];
        for (var x = 0; x < width; x++) { row[x * 4] = r; row[x * 4 + 1] = g; row[x * 4 + 2] = b; row[x * 4 + 3] = a; }
        for (var y = 0; y < height; y++) tif.WriteScanline(row, y);
    }

    /// <summary>Inserts a minimal EXIF APP1 segment carrying the given orientation right after the JPEG SOI marker.</summary>
    public static void SetJpegOrientation(string path, ushort orientation)
    {
        var jpeg = File.ReadAllBytes(path);
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)orientation, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        File.WriteAllBytes(path, [.. jpeg.AsSpan(0, 2), .. app1, .. jpeg.AsSpan(2)]);
    }
}
