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
        var provider = new CallThenTextProvider(
            callText: "creating",
            calls: [new ModelToolCall("fs_write", toolArgs)],
            finalText: "done");
        var loop = Loop(provider, new AutoApprovalGate());

        var messages = await loop.RunAsync([], "write a file");

        var tool = messages.First(m => m.Author == Author.Tool);
        Assert.Equal("fs_write", tool.ToolName);
        Assert.True(tool.Ok);
        Assert.StartsWith("Created", tool.Text);
        var final = messages.Last(m => m.Author == Author.Assistant);
        Assert.Equal("done", final.Text);
    }

    /// <summary>Round 1 carries calls; round 2 sees results and answers in text.</summary>
    private sealed class CallThenTextProvider(
        string callText, IReadOnlyList<ModelToolCall> calls, string finalText) : IModelProvider
    {
        private int _round;

        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
        {
            return _round++ == 0
                ? Task.FromResult(new ModelReply(callText, calls))
                : Task.FromResult(new ModelReply(finalText, []));
        }
    }

    [Fact]
    public async Task Refused_approval_becomes_a_failed_tool_message()
    {
        var provider = new CallThenTextProvider(
            callText: "writing",
            calls: [new ModelToolCall("fs_write", new Dictionary<string, object?> { ["path"] = "a.txt", ["content"] = "x" })],
            finalText: "sorry");
        var loop = Loop(provider, new DenyApprovalGate());

        var messages = await loop.RunAsync([], "write a file");

        var tool = messages.First(m => m.Author == Author.Tool);
        Assert.False(tool.Ok);
        Assert.Contains("refused", tool.Text);
        Assert.Equal("sorry", messages.Last(m => m.Author == Author.Assistant).Text);
    }

    [Fact]
    public async Task Tool_results_are_sent_back_and_the_final_answer_arrives()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cothon-loop2-{Guid.NewGuid():N}");
        var registry = new ToolRegistry();
        registry.Register(new FileTool(new PathPolicy(root)));
        var loop = new AgentLoop(
            new TwoRoundProvider(),
            new ToolBroker(registry, new PolicyEngine(), new AutoApprovalGate(), new UndoLedger()));

        var messages = await loop.RunAsync([], "save and tell me");

        var tool = messages.First(m => m.Author == Author.Tool);
        Assert.True(tool.Ok);
        var final = messages.Last(m => m.Author == Author.Assistant);
        Assert.Equal("the file is saved, done", final.Text);

        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Endless_callers_hit_the_round_cap_and_still_get_a_text_answer()
    {
        var registry = new ToolRegistry();
        registry.Register(new EndlessTool("fs_write"));
        var loop = new AgentLoop(
            new ScriptedProvider("whatever", [new ModelToolCall("fs_write", new Dictionary<string, object?> { ["path"] = "p", ["content"] = "x" })]),
            new ToolBroker(registry, new PolicyEngine(), new AutoApprovalGate(), new UndoLedger()));

        var messages = await loop.RunAsync([], "loop me");

            Assert.Equal(AgentLoop.MaxToolRounds, messages.Count(m => m.Author == Author.Tool));
            Assert.True(messages.Last().Author == Author.Assistant);
    }

    [Fact]
    public async Task Restricted_final_round_sends_no_tool_specs()
    {
        var provider = new RequestRecordingProvider();
        var registry = new ToolRegistry();
        registry.Register(new EndlessTool("fs_write"));
        var loop = new AgentLoop(
            provider,
            new ToolBroker(registry, new PolicyEngine(), new AutoApprovalGate(), new UndoLedger()));

        await loop.RunAsync([], "loop me");

        var last = provider.Requests[^1];
        Assert.Equal(0, (last.Tools ?? []).Count);
        Assert.True(last.ToolSteps.Count > 0);
    }

    private sealed class ScriptedProvider(string replyText, IReadOnlyList<ModelToolCall> calls) : IModelProvider
    {
        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelReply(replyText, calls));
    }

    /// <summary>Round 1: calls fs_write; round 2: answers after seeing the result.</summary>
    private sealed class TwoRoundProvider : IModelProvider
    {
        private int _round;

        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
        {
            var round = _round++;
            if (round == 0)
                return Task.FromResult(new ModelReply("saving",
                    [new ModelToolCall("fs_write", new Dictionary<string, object?> { ["path"] = "step.txt", ["content"] = "hi" }) { Id = "c1" }]));
            Assert.Contains("step.txt", request.ToolSteps[^1].Calls[0].Outcome);
            System.Diagnostics.Debug.WriteLine("round2 saw: " + request.ToolSteps[^1].Calls[0].Outcome);
            return Task.FromResult(new ModelReply("the file is saved, done", []));
        }
    }

    private sealed class EndlessTool(string name) : ITool
    {
        public ToolDefinition Definition { get; } = new()
        {
            Name = name, Description = "always calls", Reversible = false, RequiresApproval = false,
        };

        public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct)
            => Task.FromResult(ToolResult.Succeed(null, "ok"));
    }

    private sealed class RequestRecordingProvider : IModelProvider
    {
        public List<ModelRequest> Requests { get; } = [];

        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ModelReply("", [new ModelToolCall("fs_write", new Dictionary<string, object?> { ["path"] = "p", ["content"] = "x" })]));
        }
    }
}