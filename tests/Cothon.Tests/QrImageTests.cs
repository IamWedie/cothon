using QRCoder;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// QRCoder's PngByteQRCode is pure managed: the PNG signature is proof of a
/// real render without dragging a render platform into the test host.
/// </summary>
public class QrImageTests
{
    [Fact]
    public void Android_pairing_payload_becomes_a_real_png()
    {
        var png = PngOf("WIFI:T:ADB;S:QT07beCs;P:26Zbjn8kU0;;");

        Assert.True(png.Length > 200);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[0..4]);
    }

    [Fact]
    public void Distinct_secrets_render_distinct_codes()
    {
        var a = PngOf("WIFI:T:ADB;S:QT07beCs;P:26Zbjn8kU0;;");
        var b = PngOf("WIFI:T:ADB;S:QU6vTgQp;P:Zz8kLp10WX;;");

        Assert.Equal(a.Length, b.Length);
        Assert.NotEqual(a, b);
    }

    private static byte[] PngOf(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);
        return new PngByteQRCode(data).GetGraphic(10);
    }
}
