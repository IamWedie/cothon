using Cothon.Core.Policy;
using Cothon.Core.Tools;
using Cothon.Core.Undo;
using Xunit;

public class ToolBrokerTests
{
    private static ToolDefinition Def(string name, bool requiresApproval = false, bool reversible = false) => new()
    {
        Name = name,
        Description = "test",
        Risk = requiresApproval ? RiskLevel.High : RiskLevel.Low,
        Reversible = reversible,
        RequiresApproval = requiresApproval,
    };

    private static (ToolBroker Broker, List<string> Calls) Stub(ToolDefinition def, IApprovalGate gate)
    {
        var calls = new List<string>();
        var registry = new ToolRegistry();
        registry.Register(new StubTool(def, calls));
        var broker = new ToolBroker(registry, new PolicyEngine(), gate, new UndoLedger());
        return (broker, calls);
    }

    private sealed class StubTool(ToolDefinition def, List<string> calls) : ITool
    {
        public ToolDefinition Definition { get; } = def;

        public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct)
        {
            calls.Add(Definition.Name);
            return Task.FromResult(ToolResult.Succeed(null, "ran", _ => Task.CompletedTask));
        }
    }

    [Fact]
    public async Task Unknown_tool_returns_failure_without_executing()
    {
        var (broker, calls) = Stub(Def("real"), new AutoApprovalGate());
        var result = await broker.InvokeAsync("ghost");
        Assert.False(result.Ok);
        Assert.Equal("unknown_tool", result.Error!.Code);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Missing_required_argument_is_denied_by_policy()
    {
        var def = new ToolDefinition { Name = "needs_arg", Parameters = [new ToolParameter { Name = "path", Required = true }] };
        var (broker, calls) = Stub(def, new AutoApprovalGate());
        var result = await broker.InvokeAsync("needs_arg");
        Assert.False(result.Ok);
        Assert.Equal("policy_denied", result.Error!.Code);
        Assert.Contains("path", result.Outcome);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Approval_refused_means_the_tool_never_runs()
    {
        var (broker, calls) = Stub(Def("dangerous", requiresApproval: true), new DenyApprovalGate());
        var result = await broker.InvokeAsync("dangerous");
        Assert.False(result.Ok);
        Assert.Equal("approval_refused", result.Error!.Code);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Approval_granted_lets_the_tool_run()
    {
        var (broker, calls) = Stub(Def("dangerous", requiresApproval: true), new AutoApprovalGate());
        var result = await broker.InvokeAsync("dangerous");
        Assert.True(result.Ok);
        Assert.Equal(["dangerous"], calls);
    }
}