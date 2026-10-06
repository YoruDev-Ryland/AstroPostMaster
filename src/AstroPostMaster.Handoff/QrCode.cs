using QRCoder;

namespace AstroPostMaster.Handoff;

public static class QrCode
{
    public static byte[] Png(string text, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
