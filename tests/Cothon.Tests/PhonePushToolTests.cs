using Cothon.Core.Files;
using Cothon.Core.Policy;
using Cothon.Core.Tools;
using Cothon.Core.Undo;
using Cothon.Core.Phone;
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
        var tools = new PhoneTool(
            new PhoneBridge(_adb, new PathPolicy(_sandbox)));
        var registry = new ToolRegistry();
        registry.Register(tools);
        _broker = new ToolBroker(registry, new PolicyEngine(), new AutoApprovalGate(), new UndoLedger());
    }

    public void Dispose()
    {
        Directory.Delete(_sandbox, true);
    }

    [Fact]
    public async Task Push_runs_adb_with_pinned_device_and_canonical_local_path()
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

    private sealed class PhoneBridge(StubAdb adb, PathPolicy paths)
    {
        public async Task<ToolResult> PushAsync(string local, string remote, CancellationToken ct)
        {
            var verdict = paths.Validate(local);
            if (!verdict.Allowed)
                return ToolResult.Fail("path_denied", verdict.Reason!);
            if (!File.Exists(verdict.Canonical))
                return ToolResult.Fail("not_found", $"Local file not found: {verdict.Canonical}");

            var outcome = await adb.PushAsync(verdict.Canonical, remote, ct);
            return outcome.IsFailed
                ? ToolResult.Fail("adb_failed", $"adb error: {adb.ErrorMessage}")
                : ToolResult.Succeed(new { local = verdict.Canonical, remote }, $"Pushed {verdict.Canonical} → {remote}");
        }

        public ToolDefinition PushTool() => new()
        {
            Name = "phone_push_file",
            Description = "Copy a local PC file to the phone.",
            Parameters =
            [
                new ToolParameter { Name = "local", Required = true },
                new ToolParameter { Name = "remote", Description = "Phone path (default /sdcard/Download/)" },
            ],
            Risk = RiskLevel.High,
            Capabilities = ["phone.push", "fs.read"],
            Reversible = false,
        };
    }

    private sealed class PhoneTool(PhoneBridge bridge) : ITool
    {
        public ToolDefinition Definition { get; } = bridge.PushTool();

        public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct)
        {
            var local = args.TryGetValue("local", out var l) ? l as string ?? "" : "";
            var remote = args.TryGetValue("remote", out var r) && r is string rs && rs.Length > 0
                ? rs
                : "/sdcard/Download/" + Path.GetFileName(local);
            return await bridge.PushAsync(local, remote, ct);
        }
    }

    private sealed class StubAdb : AdbClient
    {
        public string ErrorMessage = "";
        public Func<string, string, CancellationToken, Task<AdbOutcome>>? OnPush;

        public StubAdb() : base(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "host") { }

        public void SimulateFailure(string message) => ErrorMessage = message;

        public async Task<AdbOutcome> PushAsync(string local, string remote, CancellationToken ct)
            => OnPush is null ? AdbOutcome.Ok("pushed") : await OnPush(local, remote, ct);
    }
}
