using System.Security.Cryptography;

namespace Cothon.Core.Phone;

/// <summary>
/// The scanning side of Android's QR pairing: a one-time name and secret the
/// phone will trust exactly once. The payload is the standard Android QR
/// dialect (<c>WIFI:T:ADB;S:name;P:secret;;</c>).
/// </summary>
public sealed record PhonePairingSecret(string Name, string Password)
{
    public string Payload => $"WIFI:T:ADB;S:{Name};P:{Password};;";

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static PhonePairingSecret Generate()
    {
        return new PhonePairingSecret(Secret(8), Secret(10));
    }

    private static string Secret(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        return new string([.. bytes.Select(b => Alphabet[b % Alphabet.Length])]);
    }
}
