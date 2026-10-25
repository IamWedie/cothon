using System.Text;
using System.Security.Cryptography;

namespace Cothon.Core.Settings;

/// <summary>
/// API keys never touch settings.json in plaintext: DPAPI (CurrentUser)
/// means only the same Windows user on this machine can read them back.
/// </summary>
internal static class KeyVault
{
    private static readonly byte[] Entropy = "Cothon::keyvault::v1"u8.ToArray();

    public static string Protect(string plain)
    {
        var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(sealedBytes);
    }

    public static string Unprotect(string cipher)
    {
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return ""; // tampered or foreign-machine cipher: treat as unset, never crash
        }
    }
}
