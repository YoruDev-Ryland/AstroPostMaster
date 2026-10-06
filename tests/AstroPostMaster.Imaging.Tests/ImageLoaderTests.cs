using SkiaSharp;

namespace AstroPostMaster.Imaging.Tests;

public class ImageLoaderTests
{
    [Fact]
    public void Load_ReadsJpegAtFullSize()
    {
        using var dir = new TempDir();
        var path = dir.File("a.jpg");
        Fixtures.WriteRgb(path, 300, 200, (_, _) => (10, 20, 30));

        using var image = ImageLoader.Load(path);

        Assert.Equal((300, 200), (image.Bitmap.Width, image.Bitmap.Height));
        Assert.Equal((300, 200), (image.SourceWidth, image.SourceHeight));
        Assert.Equal(1.0, image.Scale);
        Assert.Equal((300, 200), ImageLoader.ReadSize(path));
    }

    [Fact]
    public void Load_DecodesLargeJpegAtReducedScale()
    {
        using var dir = new TempDir();
        var path = dir.File("big.jpg");
        Fixtures.WriteRgb(path, 6000, 4000, (x, _) => ((byte)(x % 256), 0, 0));

        using var image = ImageLoader.Load(path, minScale: 0.2);

        Assert.Equal(6000, image.SourceWidth);
        Assert.InRange(image.Bitmap.Width, 1200, 5999);
        Assert.True(image.Scale >= 0.2);
    }

    [Fact]
    public void Load_GrayscalePng_DecodesToNeutralRgba()
    {
        using var dir = new TempDir();
        var path = dir.File("gray.png");
        Fixtures.WriteGray(path, 50, 40, 128);

        using var image = ImageLoader.Load(path);
        var c = image.Bitmap.GetPixel(25, 20);

        Assert.Equal(SKColorType.Rgba8888, image.Bitmap.ColorType);
        Assert.Equal((128, 128, 128), (c.Red, c.Green, c.Blue));
    }

    [Fact]
    public void Load_16BitTiff_ScalesTo8Bit()
    {
        using var dir = new TempDir();
        var path = dir.File("deep.tif");
        Fixtures.WriteTiff16(path, 64, 48, 0xFFFF, 0x8000, 0x0000);

        using var image = ImageLoader.Load(path);
        var c = image.Bitmap.GetPixel(10, 10);

        Assert.Equal((64, 48), (image.SourceWidth, image.SourceHeight));
        Assert.Equal((64, 48), ImageLoader.ReadSize(path));
        Assert.InRange(c.Red, 254, 255);
        Assert.InRange(c.Green, 126, 129);
        Assert.InRange(c.Blue, 0, 1);
    }

    [Fact]
    public void Load_KeepsEmbeddedAdobeRgbColorSpace()
    {
        using var dir = new TempDir();
        var path = dir.File("adobe.png");
        Fixtures.WriteRgb(path, 40, 40, (_, _) => (40, 160, 60), SKEncodedImageFormat.Png, Fixtures.AdobeRgb);

        using var image = ImageLoader.Load(path);

        Assert.NotNull(image.Bitmap.ColorSpace);
        Assert.False(image.Bitmap.ColorSpace!.IsSrgb);
    }

    [Fact]
    public void Load_HandlesSpacesAmpersandsAndUnicodeInPath()
    {
        using var dir = new TempDir();
        var path = dir.File("Messier 106 & Friends Hα.jpg");
        Fixtures.WriteRgb(path, 20, 10, (_, _) => (1, 2, 3));

        using var image = ImageLoader.Load(path);

        Assert.Equal(20, image.Bitmap.Width);
    }

    [Fact]
    public void Load_CorruptFile_ThrowsImageLoadExceptionNamingTheFile()
    {
        using var dir = new TempDir();
        var path = dir.File("broken.jpg");
        File.WriteAllText(path, "definitely not an image");

        var ex = Assert.Throws<ImageLoadException>(() => ImageLoader.Load(path));
        Assert.Contains("broken.jpg", ex.Message);
    }

    [Fact]
    public void Load_MissingFile_ThrowsFileNotFound()
    {
        using var dir = new TempDir();
        Assert.Throws<FileNotFoundException>(() => ImageLoader.Load(dir.File("nope.jpg")));
    }

    [Fact]
    public void LoadPreview_CapsLongEdge_AndRemembersSourceSize()
    {
        using var dir = new TempDir();
        var path = dir.File("wide.jpg");
        Fixtures.WriteRgb(path, 3000, 1000, (_, _) => (5, 5, 5));

        using var preview = ImageLoader.LoadPreview(path, maxLongEdge: 1000);

        Assert.Equal(1000, preview.Bitmap.Width);
        Assert.InRange(preview.Bitmap.Height, 332, 334);
        Assert.Equal((3000, 1000), (preview.SourceWidth, preview.SourceHeight));
    }

    [Fact]
    public void Load_TiffHonoursMinScale()
    {
        using var dir = new TempDir();
        var path = dir.File("big.tif");
        Fixtures.WriteTiff16(path, 800, 600, 0x8000, 0x8000, 0x8000);

        using var image = ImageLoader.Load(path, minScale: 0.25);

        Assert.Equal(800, image.SourceWidth);
        Assert.InRange(image.Bitmap.Width, 200, 400);
        Assert.True(image.Scale >= 0.25);
    }

    [Fact]
    public void Load_AppliesExifOrientation()
    {
        using var dir = new TempDir();
        var path = dir.File("rotated.jpg");
        Fixtures.WriteRgb(path, 300, 200, (x, _) => x < 150 ? ((byte)220, (byte)20, (byte)20) : ((byte)20, (byte)20, (byte)220));
        Fixtures.SetJpegOrientation(path, 6); // display rotated 90° clockwise

        using var image = ImageLoader.Load(path);

        Assert.Equal((200, 300), ImageLoader.ReadSize(path));
        Assert.Equal((200, 300), (image.Bitmap.Width, image.Bitmap.Height));
        Assert.Equal((200, 300), (image.SourceWidth, image.SourceHeight));
        Assert.True(image.Bitmap.GetPixel(100, 50).Red > 150, "left half of the stored image should be on top");
        Assert.True(image.Bitmap.GetPixel(100, 250).Blue > 150, "right half of the stored image should be at the bottom");
    }

    [Fact]
    public void AsImage_SharesPixelsWithTheBitmap_AndIsCached()
    {
        using var dir = new TempDir();
        var path = dir.File("a.png");
        Fixtures.WriteRgb(path, 64, 48, (_, _) => (1, 2, 3), SKEncodedImageFormat.Png);

        using var loaded = ImageLoader.Load(path);
        var image = loaded.AsImage();

        Assert.Same(image, loaded.AsImage());
        Assert.Equal(loaded.Bitmap.GetPixels(), image.PeekPixels().GetPixels());
    }
}
