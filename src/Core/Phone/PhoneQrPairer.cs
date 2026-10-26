namespace Cothon.Core.Phone;

public sealed record PhonePairOutcome(bool Paired, string Note, string ConnectHost = "", int ConnectPort = 0);

/// <summary>
/// Orchestrates QR pairing: poll `adb mdns services` until the phone
/// (after scanning the QR) advertises ITS pairing service named after our
/// secret, verify `adb pair` with that secret, then `adb connect` the live
/// endpoint found alongside. Every failure gets one honest note, never a
/// silent shrug. Production wiring is <see cref="PhoneQrPairer.Production(AdbClient)"/>.
/// </summary>
public sealed class PhoneQrPairer(
    Func<Task<AdbOutcome>> pollMdns,
    Func<string, string, Task<string>> pair,
    Func<string, Task<string>> connect,
    int maxPolls = 60,
    int pollDelayMs = 2000)
{
    public static PhoneQrPairer Production(AdbClient adb) => new(
        pollMdns: () => adb.RunAsync(["mdns", "services"], timeoutSeconds: 10, targetDevice: false),
        pair: async (pairHostPort, secret) =>
        {
            var attempt = await adb.RunAsync(["pair", pairHostPort, secret], timeoutSeconds: 12, targetDevice: false).ConfigureAwait(false);
            if (attempt.IsFailed)
                return LastLine(adb.LastError);
            if (attempt.Stdout.Contains("successfully", StringComparison.OrdinalIgnoreCase))
                return "";
            return LastLine(attempt.Stdout.Length > 0 ? attempt.Stdout : adb.LastError);
        },
        connect: async hostPort =>
        {
            var attempt = await adb.RunAsync(["connect", hostPort], timeoutSeconds: 12, targetDevice: false).ConfigureAwait(false);
            if (attempt.IsFailed)
                return LastLine(adb.LastError);
            return attempt.Stdout.Contains("connected", StringComparison.OrdinalIgnoreCase)
                ? ""
                : LastLine(attempt.Stdout.Length > 0 ? attempt.Stdout : adb.LastError);
        },
        maxPolls: 60,
        pollDelayMs: 2000);

    private static string LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : text;
    }

    public async Task<PhonePairOutcome> PairByQrAsync(PhonePairingSecret secret, CancellationToken ct)    {
        AdbOutcome scan = AdbOutcome.Failure;
        (string Host, int Port)? pairEndpoint = null;

        for (var attempt = 0; attempt < maxPolls; attempt++)
        {
            scan = await pollMdns().ConfigureAwait(false);
            pairEndpoint = scan.IsFailed ? null : MdnsPairFinder.FindPairingService(scan.Stdout, secret.Name);
            if (pairEndpoint is not null)
                break;
            if (pollDelayMs > 0)
                await Task.Delay(pollDelayMs, ct).ConfigureAwait(false);
        }

        if (pairEndpoint is null)
        {
            var note = scan.IsFailed ? "adb errored" : "no services at all";
            return new PhonePairOutcome(false, $"The phone never advertised the pairing service for this QR ({note}).");
        }

        var pairMessage = await pair($"{pairEndpoint.Value.Host}:{pairEndpoint.Value.Port}", secret.Password).ConfigureAwait(false);
        if (pairMessage.Length > 0)
            return new PhonePairOutcome(false, $"Pairing was refused: {pairMessage}");

        // After pairing, discover the live endpoint on the same host (fresh scan; the pairing service disappears).
        (string Host, int Port)? liveEndpoint = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var after = await pollMdns().ConfigureAwait(false);
            if (!after.IsFailed)
            {
                liveEndpoint = MdnsPairFinder.FindConnectOnHost(after.Stdout, pairEndpoint.Value.Host);
                if (liveEndpoint is not null)
                    break;
            }
            if (pollDelayMs > 0)
                await Task.Delay(pollDelayMs, ct).ConfigureAwait(false);
        }
        if (liveEndpoint is null)
            return new PhonePairOutcome(
                true,
                "Paired with the phone, but it stopped advertising a connect endpoint before Cothon could lock it. Read Wireless debugging's IP & port into Settings.");

        var connectMessage = await connect($"{liveEndpoint.Value.Host}:{liveEndpoint.Value.Port}").ConfigureAwait(false);
        if (connectMessage.Length > 0 && !connectMessage.Contains("already", StringComparison.OrdinalIgnoreCase))
            return new PhonePairOutcome(true, $"Paired, but the first connect failed: {connectMessage}",
                liveEndpoint.Value.Host, liveEndpoint.Value.Port);

        return new PhonePairOutcome(true, "Paired and connected.",
            liveEndpoint.Value.Host, liveEndpoint.Value.Port);
    }
}
