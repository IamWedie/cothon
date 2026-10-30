using Cothon.Core.Phone;
using Xunit;

public class PhoneEndpointHealTests
{
    [Fact]
    public async Task Push_over_a_dead_endpoint_heals_from_mdns_and_succeeds()
    {
        var endpoints = new List<string>();
        var healedTo = (string.Empty, 0);
        var adb = new StubAdb("192.168.1.20", 5555);
        adb.OnPush = (_, _, _) =>
        {
            endpoints.Add(adb.DeviceId);
            return adb.DeviceId.StartsWith("192.168.1.20")
                ? adb.SimulateTrouble("cannot connect to 192.168.1.20:5555: since when?")
                : Task.FromResult(AdbOutcome.Ok("streamed note.txt"));
        };
        var polls = 0;
        var phone = new HealingPhone(adb, "adb.exe",
            mdnsPoll: () =>
            {
                polls++;
                return Task.FromResult<(string, int)?>(
                    polls == 1 ? new("192.168.1.127", 43475) : null);
            },
            onHealed: (h, p) => healedTo = (h, p),
            pollDelayMs: 0,
            healedClientBuilder: endpoint => new StubAdb(endpoint.Host, endpoint.Port)
            {
                OnPush = (_, _, _) => Task.FromResult(AdbOutcome.Ok("streamed note.txt")),
            });

        var outcome = await phone.PushAsync("C:\\tmp\\note.txt", "/sdcard/Download/note.txt");

        Assert.False(outcome.IsFailed);
        Assert.Contains("streamed note.txt", outcome.Stdout);
        Assert.Equal(["192.168.1.20:5555"], endpoints);
        Assert.Equal("192.168.1.127", healedTo.Item1);
        Assert.Equal(43475, healedTo.Item2);
    }

    [Fact]
    public async Task A_healthy_endpoint_never_takes_the_mdns_dive()
    {
        var adb = new StubAdb("192.168.1.127", 43475)
        {
            OnPush = (_, _, _) => Task.FromResult(AdbOutcome.Ok("pushed")),
        };
        var polls = 0;
        var phone = new HealingPhone(adb, "adb.exe",
            mdnsPoll: () => { polls++; return Task.FromResult<(string, int)?>(null); },
            pollDelayMs: 0);

        var outcome = await phone.PushAsync("a.txt", "/sdcard/Download/a.txt");

        Assert.False(outcome.IsFailed);
        Assert.Equal(0, polls);
    }

    [Fact]
    public async Task A_plain_push_error_is_not_a_transport_problem()
    {
        var adb = new StubAdb("192.168.1.127", 43475);
        adb.OnPush = (_, _, _) =>
        {
            adb.FailWith("adb: error: failed to stat remote object '/sdcard/Download/x': No such file or directory");
            return Task.FromResult(AdbOutcome.Failure);
        };
        var polls = 0;
        var phone = new HealingPhone(adb, "adb.exe",
            mdnsPoll: () => { polls++; return Task.FromResult<(string, int)?>(null); },
            pollDelayMs: 0);

        var outcome = await phone.PushAsync("x.txt", "/sdcard/Download/x.txt");

        Assert.True(outcome.IsFailed);
        Assert.True(polls == 0, "a nonexistent remote file is not a dead phone");
        Assert.Contains("No such file or directory", phone.LastError);
    }

    [Fact]
    public async Task Mdns_silence_keeps_the_original_failure()
    {
        var healedAny = false;
        var adb = new StubAdb("192.168.1.20", 5555);
        adb.OnPush = (_, _, _) =>
            adb.SimulateTrouble("cannot connect to 192.168.1.20:5555: unreachable");
        var polls = 0;
        var phone = new HealingPhone(adb, "adb.exe",
            mdnsPoll: () => { polls++; return Task.FromResult<(string, int)?>(null); },
            onHealed: (_, _) => healedAny = true,
            pollDelayMs: 0);

        var outcome = await phone.PushAsync("a.txt", "/sdcard/Download/a.txt");

        Assert.True(outcome.IsFailed);
        Assert.False(healedAny);
        Assert.Equal(HealingPhone.DefaultMaxPolls, polls);
        Assert.Contains("unreachable", phone.LastError);
    }

    [Fact]
    public async Task Healing_never_rolls_more_than_once()
    {
        var endpoints = new List<string>();
        var healed = 0;
        var adb = new StubAdb("192.168.1.20", 5555);
        adb.OnPush = (_, _, _) =>
        {
            endpoints.Add(adb.DeviceId);
            return adb.DeviceId.StartsWith("192.168.1.20")
                ? adb.SimulateTrouble("cannot connect to 192.168.1.20:5555: dead")
                : adb.SimulateTrouble("cannot connect to 192.168.1.127:43475: dead too");
        };
        StubAdb? fresh = null;
        var freshTries = 0;
        var phone = new HealingPhone(adb, "adb.exe",
            mdnsPoll: () => Task.FromResult<(string, int)?>(new("192.168.1.127", 43475)),
            onHealed: (_, _) => healed++,
            pollDelayMs: 0,
            healedClientBuilder: endpoint => fresh = new StubAdb(endpoint.Host, endpoint.Port)
            {
                OnPush = (_, _, _) =>
                {
                    freshTries++;
                    return fresh!.SimulateTrouble("cannot connect to 192.168.1.127:43475: dead too");
                },
            });

        var outcome = await phone.PushAsync("a.txt", "/sdcard/Download/a.txt");

        Assert.True(outcome.IsFailed);
        Assert.Equal("192.168.1.127:43475", fresh!.DeviceId);
        Assert.True(freshTries == 1, "one original try, one healed try, no loop");
        Assert.True(healed == 0, "memory only records true health");
        Assert.Contains("dead too", phone.LastError);
    }

    [Fact]
    public async Task Connect_heals_a_stale_port_too()
    {
        var healedTo = (string.Empty, 0);
        var adb = new StubAdb("192.168.1.20", 5555);
        adb.OnConnect = device => device.StartsWith("192.168.1.20")
            ? adb.SimulateTroubleForConnect("cannot connect to 192.168.1.20:5555")
            : Task.FromResult(true);
        StubAdb? fresh = null;
        var phone = new HealingPhone(adb, "adb.exe",
            mdnsPoll: () => Task.FromResult<(string, int)?>(new("192.168.1.127", 43475)),
            onHealed: (h, p) => healedTo = (h, p),
            pollDelayMs: 0,
            healedClientBuilder: endpoint => fresh = new StubAdb(endpoint.Host, endpoint.Port)
            {
                OnConnect = _ => Task.FromResult(true),
            });

        var ok = await phone.ConnectAsync();

        Assert.True(ok);
        Assert.Equal("192.168.1.127:43475", fresh!.DeviceId);
        Assert.Equal("192.168.1.127", healedTo.Item1);
        Assert.Equal(43475, healedTo.Item2);
    }

    // ── mDNS chooser rules ───────────────────────────────────────────────

    [Fact]
    public void Chooser_prefers_the_known_host_even_among_strangers()
    {
        var output = "A1\t_adb-tls-connect._tcp\t192.168.1.90:7777\n" +
                     "A2\t_adb-tls-connect._tcp\t192.168.1.20:43921";
        var pick = MdnsPairFinder.ChooseConnectEndpoint(output, "192.168.1.20");
        Assert.Equal(("192.168.1.20", 43921), pick);
    }

    [Fact]
    public void Chooser_accepts_a_sole_foreign_service_when_host_went_silent()
    {
        var output = "A1\t_adb-tls-connect._tcp\t192.168.1.127:43475";
        var pick = MdnsPairFinder.ChooseConnectEndpoint(output, "192.168.1.20");
        Assert.Equal(("192.168.1.127", 43475), pick);
    }

    [Fact]
    public void Chooser_refuses_to_guess_between_strangers()
    {
        var output = "A1\t_adb-tls-connect._tcp\t192.168.1.90:7777\n" +
                     "A2\t_adb-tls-connect._tcp\t192.168.1.91:7778";
        var pick = MdnsPairFinder.ChooseConnectEndpoint(output, "192.168.1.20");
        Assert.Null(pick);
    }

    [Fact]
    public void Chooser_ignores_services_of_other_kinds()
    {
        var output = "A1\t_adb-tls-pairing._tcp\t192.168.1.20:9999\n" +
                     "A2\t_adb-tls-connect._tcp\t192.168.1.20:43921";
        var pick = MdnsPairFinder.ChooseConnectEndpoint(output, "192.168.1.20");
        Assert.Equal(("192.168.1.20", 43921), pick);
    }

    // ── fakes ─────────────────────────────────────────────────────────────

    private sealed class StubAdb(string host, int port) : AdbClient("adb.exe", host, port)
    {
        public Func<string, string, CancellationToken, Task<AdbOutcome>>? OnPush;
        public Func<string, Task<bool>>? OnConnect;

        public void FailWith(string message) => LastError = message;

        public Task<AdbOutcome> SimulateTrouble(string message)
        {
            LastError = message;
            return Task.FromResult(AdbOutcome.Failure);
        }

        public async Task<bool> SimulateTroubleForConnect(string message)
        {
            LastError = message;
            await Task.CompletedTask;
            return false;
        }

        public override Task<AdbOutcome> PushAsync(string local, string remote, CancellationToken ct = default)
            => OnPush is null ? Task.FromResult(AdbOutcome.Ok("pushed")) : OnPush(local, remote, ct);

        public override Task<bool> ConnectAsync(CancellationToken ct = default)
            => OnConnect is null ? base.ConnectAsync(ct) : OnConnect(DeviceId);
    }
}
