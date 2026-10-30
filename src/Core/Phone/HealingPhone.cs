namespace Cothon.Core.Phone;

/// <summary>
/// Wireless-debugging ports are rented, not owned: Android hands out a new
/// connect port every time the phone restarts Wireless debugging, so a held
/// endpoint goes stale overnight. This wrapper notices the dead transport
/// (and only then — a wrong filename is not a dead phone), asks mDNS for the
/// phone's fresh broadcast, retries exactly once on the new endpoint, and
/// remembers the live address so the next launch starts healthy.
/// </summary>
public sealed class HealingPhone : IDeviceConnector, IPhonePusher
{
    public const int DefaultMaxPolls = 4;

    /// <summary>Only phrases meaning "the transport died", never "the file was wrong".</summary>
    public static readonly string[] TransportTroubleTokens =
    [
        "cannot connect",
        "failed to connect",
        "not found",
        "offline",
        "timed out",
        "unreachable",
    ];

    private readonly AdbClient _inner;
    private readonly Func<(string Host, int Port), AdbClient> _buildHealed;
    private readonly Func<Task<(string Host, int Port)?>> _mdnsPoll;
    private readonly Action<string, int>? _onHealed;
    private readonly int _maxPolls;
    private readonly int _pollDelayMs;
    private string _lastNote = "";
    private string _lastError = "";

    /// <summary>The newest known truth: the healed attempt's error outranks the first one.</summary>
    public string LastError => _lastError.Length > 0 ? _lastError : _inner.LastError;

    public HealingPhone(
        AdbClient inner,
        string adbPath,
        Func<Task<(string Host, int Port)?>> mdnsPoll,
        Action<string, int>? onHealed = null,
        int maxPolls = DefaultMaxPolls,
        int pollDelayMs = 2500,
        Func<(string Host, int Port), AdbClient>? healedClientBuilder = null)
    {
        _inner = inner;
        _buildHealed = healedClientBuilder ?? (endpoint => new AdbClient(adbPath, endpoint.Host, endpoint.Port));
        _mdnsPoll = mdnsPoll;
        _onHealed = onHealed;
        _maxPolls = maxPolls;
        _pollDelayMs = pollDelayMs;
    }

    /// <summary>Production healer: polls `adb mdns services` from an unpinned transport.</summary>
    public static HealingPhone For(AdbClient inner, string adbPath, Action<string, int>? onHealed = null)
    {
        var unpinned = new AdbClient(adbPath, "");
        var preferredHost = inner.Host;
        return new HealingPhone(
            inner,
            adbPath,
            mdnsPoll: async () =>
            {
                var services = await unpinned
                    .RunAsync(["mdns", "services"], timeoutSeconds: 8, targetDevice: false)
                    .ConfigureAwait(false);
                return services.IsFailed
                    ? null
                    : MdnsPairFinder.ChooseConnectEndpoint(services.Stdout, preferredHost);
            },
            onHealed: onHealed);
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (await _inner.ConnectAsync(ct).ConfigureAwait(false))
            return true;

        return LooksLikeTransportTrouble(_inner.LastError) && await TryHealAsync(
            async healed => await healed.ConnectAsync(ct).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    public async Task<AdbOutcome> PushAsync(string local, string remote, CancellationToken ct = default)
    {
        var outcome = await _inner.PushAsync(local, remote, ct).ConfigureAwait(false);
        if (!outcome.IsFailed || !LooksLikeTransportTrouble(_inner.LastError))
            return outcome;

        var healed = await TryHealAsync(
            async fresh =>
            {
                var retry = await fresh.PushAsync(local, remote, ct).ConfigureAwait(false);
                if (!retry.IsFailed)
                    _lastNote = retry.Stdout;
                return !retry.IsFailed;
            })
            .ConfigureAwait(false);

        return healed ? AdbOutcome.Ok(_lastNote) : outcome;
    }

    /// <summary>At most one rediscovery: the fresh endpoint must work on its first try.</summary>
    private async Task<bool> TryHealAsync(Func<AdbClient, Task<bool>> attempt)
    {
        for (var poll = 0; poll < _maxPolls; poll++)
        {
            var found = await _mdnsPoll().ConfigureAwait(false);
            if (found is { } endpoint &&
                (endpoint.Host != _inner.Host || endpoint.Port != _inner.Port))
            {
            var fresh = _buildHealed(endpoint);
            if (await attempt(fresh).ConfigureAwait(false))
            {
                _onHealed?.Invoke(endpoint.Host, endpoint.Port);
                return true;
            }
            _lastError = fresh.LastError;
            return false;
            }
            if (_pollDelayMs > 0 && poll < _maxPolls - 1)
                await Task.Delay(_pollDelayMs).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>Only a lost transport earns an mDNS dive; honest errors stay honest.</summary>
    public static bool LooksLikeTransportTrouble(string lastError)
        => TransportTroubleTokens.Any(token =>
            lastError.Contains(token, StringComparison.OrdinalIgnoreCase));
}
