using Cothon.Core.Memory;
using Cothon.Core.Policy;
using Cothon.Core.Sessions;
using Cothon.Core.Tools;
using Cothon.Core.Undo;
using Xunit;

public class MemoryLoopTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cothon-mem-{Guid.NewGuid():N}.json");

    private MemoryStore Store() => new(enabled: true, path: _path);

    private static AgentLoop Loop(IModelProvider provider, MemoryStore? memory, out ScriptedProvider recorded)
    {
        var registry = new ToolRegistry();
        if (memory is not null)
            registry.Register(new MemoryTool(memory));
        recorded = provider as ScriptedProvider ?? throw new InvalidOperationException();
        return new AgentLoop(
            provider,
            new ToolBroker(registry, new PolicyEngine(), new AutoApprovalGate(), new UndoLedger()),
            memory);
    }

    [Fact]
    public async Task Known_facts_are_injected_into_the_system_prompt()
    {
        var store = Store();
        store.Remember("name", "Wadia", category: "identity");
        var loop = Loop(new ScriptedProvider("hi"), store, out var recorded);

        await loop.RunAsync([], "hello");

        Assert.Contains("You are Cothon.", recorded.Captured.SystemPrompt);
        Assert.Contains("name: Wadia", recorded.Captured.SystemPrompt);
        Assert.Contains("identity", recorded.Captured.SystemPrompt);
    }

    [Fact]
    public async Task No_facts_means_the_plain_system_prompt()
    {
        var loop = Loop(new ScriptedProvider("hi"), Store(), out var recorded);

        await loop.RunAsync([], "hello");

        Assert.Equal("You are Cothon.", recorded.Captured.SystemPrompt.Trim());
    }

    [Fact]
    public async Task Memory_save_tool_stores_what_the_model_heard()
    {
        var store = Store();
        var provider = new ScriptedProvider(
            "noted",
            calls: [new ModelToolCall("memory_save", new Dictionary<string, object?>
            {
                ["key"] = "project",
                ["value"] = "Cothon",
                ["category"] = "work",
            })]);
        var loop = Loop(provider, store, out _);

        var messages = await loop.RunAsync([], "remember Cothon is my project");

        var tool = messages.First(m => m.Author == Author.Tool);
        Assert.Equal("memory_save", tool.ToolName);
        Assert.True(tool.Ok);
        Assert.Contains("Cothon", store.RecallText("project"));
    }

    [Fact]
    public async Task Registry_tools_are_declared_to_the_model()
    {
        var store = Store();
        var loop = Loop(new ScriptedProvider("hi"), store, out var recorded);

        await loop.RunAsync([], "hello");

        var spec = Assert.Single(recorded.Captured.Tools ?? []);
        Assert.Equal("memory_save", spec.Name);
        Assert.Contains("durable fact", spec.Description);
    }

    [Fact]
    public async Task Secret_values_are_redacted_in_memory()
    {
        var store = Store();
        var provider = new ScriptedProvider(
            "noted",
            calls: [new ModelToolCall("memory_save", new Dictionary<string, object?>
            {
                ["key"] = "creds",
                ["value"] = "api_key: sk-abc123",
            })]);
        var loop = Loop(provider, store, out _);

        await loop.RunAsync([], "save my key sk-abc123");

        var fact = store.Recall().Single(f => f.Key == "creds");
        Assert.DoesNotContain("sk-abc123", fact.Value);
        Assert.Contains("REDACTED", fact.Value);
    }

    [Fact]
    public void Disabled_store_saves_nothing_and_reports_honestly()
    {
        var store = new MemoryStore(enabled: false, path: _path);
        var tool = new MemoryTool(store);

        var result = tool.ExecuteAsync(
            new Dictionary<string, object?> { ["key"] = "k", ["value"] = "v" }, default).Result;

        Assert.True(result.Ok);
        Assert.Contains("switched off", result.Outcome);
    }

    public void Dispose() => File.Delete(_path);

    private sealed class ScriptedProvider(string replyText, IReadOnlyList<ModelToolCall>? calls = null) : IModelProvider
    {
        public ModelRequest Captured { get; private set; } = null!;

        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
        {
            Captured = request;
            return Task.FromResult(new ModelReply(replyText, calls ?? []));
        }
    }
}
