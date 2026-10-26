using Avalonia.Media;
using Avalonia.Media.Imaging;
using QRCoder;

namespace Cothon.Ui;

/// <summary>One QR code for the phone's camera: Android's WIFI:T:ADB dialect.</summary>
public static class QrImage
{
    public static IImage Render(string payload, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        using var stream = new MemoryStream(png);
        return new Bitmap(stream);
    }
}
