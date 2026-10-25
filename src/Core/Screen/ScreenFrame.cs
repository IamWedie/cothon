namespace Cothon.Core.Screen;

/// <summary>One captured screen frame, encoded and ready for a vision model.</summary>
public sealed record ScreenFrame(
    IReadOnlyList<byte> Bytes,
    int Width,
    int Height,
    string MimeType);
