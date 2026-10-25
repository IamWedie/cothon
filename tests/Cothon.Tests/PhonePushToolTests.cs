using Cothon.Core.Files;
using Cothon.Core.Policy;
using Cothon.Core.Phone;
using Cothon.Core.Sessions;
using Cothon.Core.Tools;
using Cothon.Core.Undo;
using Xunit;

public class PhonePushToolTests : IDisposable
{
    private readonly string _sandbox;
    private readonly ToolBroker _broker;
    private readonly StubAdb _adb;

    public PhonePushToolTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), $"cothon-phone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sandbox);
        _adb = new StubAdb();
        var registry = new ToolRegistry();
        registry.Register(new PhoneTool(_adb, new PathPolicy(_sandbox)));
        _broker = new ToolBroker(registry, new PolicyEngine(), new AutoApprovalGate(), new UndoLedger());
    }

    public void Dispose()
    {
        Directory.Delete(_sandbox, true);
    }

    [Fact]
    public async Task Push_runs_adb_with_canonical_local_path()
    {
        var local = Path.Combine(_sandbox, "note.txt");
        await File.WriteAllTextAsync(local, "hi");
        (string Local, string Remote) sent = default;
        _adb.OnPush = (l, r, _) => { sent = (l, r); return Task.FromResult(AdbOutcome.Ok("pushed")); };

        var result = await _broker.InvokeAsync("phone_push_file", new Dictionary<string, object?>
        {
            ["local"] = "note.txt",
            ["remote"] = "/sdcard/Download/note.txt",
        });

        Assert.True(result.Ok);
        Assert.IsType<string>(sent.Local);
        Assert.Equal(local, sent.Local);
        Assert.Equal("/sdcard/Download/note.txt", sent.Remote);
    }

    [Fact]
    public async Task Push_defaults_remote_to_sdcard_download()
    {
        var local = Path.Combine(_sandbox, "note.txt");
        await File.WriteAllTextAsync(local, "hi");
        string? sentRemote = null;
        _adb.OnPush = (_, r, _) => { sentRemote = r; return Task.FromResult(AdbOutcome.Ok("pushed")); };

        await _broker.InvokeAsync("phone_push_file", new Dictionary<string, object?>
        {
            ["local"] = "note.txt",
        });

        Assert.Equal("/sdcard/Download/note.txt", sentRemote);
    }

    [Fact]
    public async Task Push_of_a_file_outside_sandbox_is_denied_and_adb_never_runs()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"cothon-phone-outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "private");
        _adb.OnPush = (_, _, _) => throw new InvalidOperationException("adb must not run");

        var result = await _broker.InvokeAsync("phone_push_file", new Dictionary<string, object?>
        {
            ["local"] = outside,
        });

        Assert.False(result.Ok);
        Assert.Equal("path_denied", result.Error!.Code);
    }

    [Fact]
    public async Task Push_of_a_missing_file_reports_not_found()
    {
        var result = await _broker.InvokeAsync("phone_push_file", new Dictionary<string, object?>
        {
            ["local"] = "nope.txt",
        });

        Assert.False(result.Ok);
        Assert.Equal("not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Adb_failure_surfaces_as_tool_failure()
    {
        var local = Path.Combine(_sandbox, "x.txt");
        await File.WriteAllTextAsync(local, "x");
        _adb.OnPush = (_, _, _) => { _adb.SimulateFailure("boom"); return Task.FromResult(AdbOutcome.Failure); };

        var result = await _broker.InvokeAsync("phone_push_file", new Dictionary<string, object?>
        {
            ["local"] = "x.txt",
        });

        Assert.False(result.Ok);
        Assert.Equal("adb_failed", result.Error!.Code);
        Assert.Contains("boom", result.Outcome);
    }

    [Fact]
    public void Definition_carries_phone_capability_and_high_risk()
    {
        var tool = new PhoneTool(new StubAdb(), new PathPolicy(_sandbox));

        Assert.Equal("phone_push_file", tool.Definition.Name);
        Assert.Equal(RiskLevel.High, tool.Definition.Risk);
        Assert.Contains("phone.push", tool.Definition.Capabilities);
    }

    private sealed class StubAdb : AdbClient
    {
        public Func<string, string, CancellationToken, Task<AdbOutcome>>? OnPush;

        public StubAdb() : base(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "host") { }

        public void SimulateFailure(string message) => LastError = message;

        public override Task<AdbOutcome> PushAsync(string local, string remote, CancellationToken ct = default)
            => OnPush is null ? Task.FromResult(AdbOutcome.Ok("pushed")) : OnPush(local, remote, ct);
    }
}
