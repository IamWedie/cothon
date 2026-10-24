using Cothon.Core.Tools;
using Xunit;

public class ToolRegistryTests
{
    private static ToolDefinition Def(string name) => new()
    {
        Name = name,
        Description = "test tool",
        Risk = RiskLevel.Low,
        Capabilities = ["fs.read"],
        Reversible = true,
        RequiresApproval = false,
    };

    private sealed class EchoTool(ToolDefinition def) : ITool
    {
        public ToolDefinition Definition { get; } = def;
        public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct)
            => Task.FromResult(ToolResult.Succeed(new { echo = args.TryGetValue("text", out var t), text = t }));
    }

    [Fact]
    public void Registers_and_resolves_a_tool()
    {
        var tool = new EchoTool(Def("echo"));
        var registry = new ToolRegistry();
        registry.Register(tool);

        Assert.Same(tool, registry.Resolve("echo"));
    }

    [Fact]
    public void Duplicate_name_is_rejected()
    {
        var registry = new ToolRegistry();
        registry.Register(new EchoTool(Def("echo")));

        Assert.Throws<InvalidOperationException>(() => registry.Register(new EchoTool(Def("echo"))));
    }

    [Fact]
    public void Unknown_tool_resolves_to_null()
    {
        Assert.Null(new ToolRegistry().Resolve("missing"));
    }
}