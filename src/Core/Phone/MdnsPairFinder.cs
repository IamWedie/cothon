namespace Cothon.Core.Phone;

/// <summary>
/// Reads `adb mdns services` output: tab-separated instance/type/host:port.
/// QR pairing handshake rides <c>_adb-tls-pairing._tcp</c>; the live device
/// endpoint announces itself as <c>_adb-tls-connect._tcp</c> on the same host.
/// </summary>
public static class MdnsPairFinder
{
    public static (string Host, int Port)? FindPairingService(string mdnsOutput, string nameToken)
        => FindService(mdnsOutput, "_adb-tls-pairing._tcp", line => line.Contains(nameToken, StringComparison.Ordinal));

    public static (string Host, int Port)? FindConnectOnHost(string mdnsOutput, string host)
        => FindService(mdnsOutput, "_adb-tls-connect._tcp", line => line.Contains(host, StringComparison.Ordinal));

    private static (string, int)? FindService(string mdnsOutput, string serviceType, Func<string, bool> match)
    {
        foreach (var line in mdnsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.Contains(serviceType, StringComparison.Ordinal))
                continue;
            if (!match(line))
                continue;

            var endpoint = line.Split('\t')[^1].Trim();
            var colon = endpoint.LastIndexOf(':');
            if (colon <= 0)
                return null;
            if (!int.TryParse(endpoint[(colon + 1)..], out var port))
                return null;
            return (endpoint[..colon], port);
        }
        return null;
    }
}
