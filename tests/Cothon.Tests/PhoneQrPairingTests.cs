using Cothon.Core.Phone;
using Xunit;

namespace Cothon.Tests;

public class PhoneQrPairingTests
{
    [Fact]
    public void Secret_is_a_valid_android_qr_payload()
    {
        var secret = new PhonePairingSecret("QT07beCs", "26Zbjn8kU0");

        Assert.Equal("WIFI:T:ADB;S:QT07beCs;P:26Zbjn8kU0;;", secret.Payload);
    }

    [Fact]
    public void Generated_secret_carries_the_name_and_a_password_without_qr_breakers()
    {
        var secret = PhonePairingSecret.Generate();

        Assert.Equal(8, secret.Name.Length);
        Assert.Equal(10, secret.Password.Length);
        Assert.StartsWith("WIFI:T:ADB;S:", secret.Payload);
        Assert.EndsWith(";;", secret.Payload);
        Assert.All(secret.Name + secret.Password, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    [Fact]
    public void Pairing_service_is_found_by_name_in_mdns_output()
    {
        var output = """
        List of discovered mdns services
        adb-A9SV6R4612001505-8S1_uv	_adb-tls-pairing._tcp	192.168.1.127:41097
        adb-A9SV6R4612001505-VL3PJJ	_adb-tls-connect._tcp	192.168.1.127:43475
        """;

        var found = MdnsPairFinder.FindPairingService(output, "8S1_uv");

        Assert.NotNull(found);
        Assert.Equal("192.168.1.127", found.Value.Host);
        Assert.Equal(41097, found.Value.Port);
    }

    [Fact]
    public void Connect_services_never_masquerade_as_pairing()
    {
        var output = "adb-A9SV6R4612001505-VL3PJJ\t_adb-tls-connect._tcp\t192.168.1.127:43475";

        Assert.Null(MdnsPairFinder.FindPairingService(output, "VL3PJJ"));
    }

    [Fact]
    public void No_match_when_name_absent()
    {
        Assert.Null(MdnsPairFinder.FindPairingService(
            "adb-A9SV6R4612001505-XXXX\t_adb-tls-pairing._tcp\t192.168.1.127:1", "MISSING"));
    }

    [Fact]
    public void Connect_endpoint_is_found_on_the_same_host()
    {
        var output = """
        adb-A9SV6R4612001505-8S1_uv	_adb-tls-pairing._tcp	192.168.1.127:41097
        adb-A9SV6R4612001505-VL3PJJ	_adb-tls-connect._tcp	192.168.1.127:43475
        """;

        var found = MdnsPairFinder.FindConnectOnHost(output, "192.168.1.127");

        Assert.NotNull(found);
        Assert.Equal(43475, found.Value.Port);
    }

    [Fact]
    public async Task Pairer_scans_pairs_connects_and_returns_endpoint()
    {
        string? paired = null;
        string? connected = null;
        var output = """
        adb-A9SV6R4612001505-8S1_uv	_adb-tls-pairing._tcp	192.168.1.127:41097
        adb-A9SV6R4612001505-VL3PJJ	_adb-tls-connect._tcp	192.168.1.127:43475
        """;

        var outcome = await new PhoneQrPairer(
            pollMdns: () => Task.FromResult(AdbOutcome.Ok(output)),
            pair: (hostPort, secret) => { paired = $"{hostPort}|{secret}"; return Task.FromResult(""); },
            connect: hostPort => { connected = hostPort; return Task.FromResult(""); },
            pollDelayMs: 0)
            .PairByQrAsync(new PhonePairingSecret("8S1_uv", "SECRET1"), CancellationToken.None);

        Assert.True(outcome.Paired, outcome.Note);
        Assert.Equal("192.168.1.127:41097|SECRET1", paired);
        Assert.Equal("192.168.1.127:43475", connected);
        Assert.Equal("192.168.1.127", outcome.ConnectHost);
        Assert.Equal(43475, outcome.ConnectPort);
    }

    [Fact]
    public async Task Pairer_reports_the_pairing_failure_honestly()
    {
        var connectCalled = false;

        var outcome = await new PhoneQrPairer(
            pollMdns: () => Task.FromResult(AdbOutcome.Ok(
                "adb-X-S\t_adb-tls-pairing._tcp\t10.0.0.9:43454")),
            pair: (_, _) => Task.FromResult("failed to pair: 4099"),
            connect: _ => { connectCalled = true; return Task.FromResult(""); },
            pollDelayMs: 0)
            .PairByQrAsync(new PhonePairingSecret("S", "SECRET1"), CancellationToken.None);

        Assert.False(outcome.Paired);
        Assert.Contains("4099", outcome.Note);
        Assert.False(connectCalled);
    }

    [Fact]
    public async Task Scanner_gives_up_honestly_when_nothing_advertises()
    {
        var outcome = await new PhoneQrPairer(
            pollMdns: () => Task.FromResult(AdbOutcome.Ok("List of discovered mdns services")),
            pair: (_, _) => Task.FromResult(""),
            connect: _ => Task.FromResult(""),
            maxPolls: 2, pollDelayMs: 0)
            .PairByQrAsync(PhonePairingSecret.Generate(), CancellationToken.None);

        Assert.False(outcome.Paired);
        Assert.Contains("never advertised", outcome.Note);
    }
}
