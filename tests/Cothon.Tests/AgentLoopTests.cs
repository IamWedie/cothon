using Cothon.Core.Files;
using Cothon.Core.Policy;
using Cothon.Core.Sessions;
using Cothon.Core.Tools;
using Cothon.Core.Undo;
using Xunit;

public class AgentLoopTests
{
    private static AgentLoop Loop(IModelProvider provider, IApprovalGate gate)
    {
        var registry = new ToolRegistry();
        registry.Register(new FileTool(new PathPolicy(Path.Combine(Path.GetTempPath(), $"cothon-loop-{Guid.NewGuid():N}"))));
        return new AgentLoop(provider, new ToolBroker(registry, new PolicyEngine(), gate, new UndoLedger()));
    }

    [Fact]
    public async Task Reply_contains_user_echo_and_assistant_text()
    {
        var loop = Loop(new EchoModelProvider(), new AutoApprovalGate());

        var messages = await loop.RunAsync([], "status check");

        Assert.Equal(2, messages.Count);
        Assert.Equal(Author.User, messages[0].Author);
        Assert.Equal("status check", messages[0].Text);
        Assert.Equal(Author.Assistant, messages[1].Author);
        Assert.Equal("[echo] status check", messages[1].Text);
    }

    [Fact]
    public async Task Tool_call_message_records_name_and_outcome()
    {
        var toolArgs = new Dictionary<string, object?> { ["path"] = "a.txt", ["content"] = "x" };
        var provider = new ScriptedProvider(
            replyText: "done",
            calls: [new ModelToolCall("fs_write", toolArgs)]);
        var loop = Loop(provider, new AutoApprovalGate());

        var messages = await loop.RunAsync([], "write a file");

        var tool = messages.First(m => m.Author == Author.Tool);
        Assert.Equal("fs_write", tool.ToolName);
        Assert.True(tool.Ok);
        Assert.StartsWith("Created", tool.Text);
        Assert.Equal(Author.Assistant, messages.Last().Author);
    }

    [Fact]
    public async Task Refused_approval_becomes_a_failed_tool_message()
    {
        var provider = new ScriptedProvider(
            "sorry",
            [new ModelToolCall("fs_write", new Dictionary<string, object?> { ["path"] = "a.txt", ["content"] = "x" })]);
        var loop = Loop(provider, new DenyApprovalGate());

        var messages = await loop.RunAsync([], "write a file");

        var tool = messages.First(m => m.Author == Author.Tool);
        Assert.False(tool.Ok);
        Assert.Contains("refused", tool.Text);
    }

    private sealed class ScriptedProvider(string replyText, IReadOnlyList<ModelToolCall> calls) : IModelProvider
    {
        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelReply(replyText, calls));
    }
}