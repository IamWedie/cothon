using Cothon.Core.Memory;
using Cothon.Core.Tools;

namespace Cothon.Core.Memory;

/// <summary>
/// Lets the model store durable user facts between sessions. Low risk:
/// nothing executes, secrets are redacted on the way in, and the human can
/// read and erase the store any time.
/// </summary>
public sealed class MemoryTool(MemoryStore store) : ITool
{
    public ToolDefinition Definition { get; } = new()
    {
        Name = "memory_save",
        Description = "Store a durable fact about the user for future sessions. Only save explicit, durable information the user asks to remember or clearly wants remembered; never secrets.",
        Parameters =
        [
            new ToolParameter { Name = "key", Required = true, Description = "Short stable label, e.g. 'coffee_order'." },
            new ToolParameter { Name = "value", Required = true, Description = "The fact itself, one line." },
            new ToolParameter { Name = "category", Description = "Rough bucket like 'personal' or 'work'. Default 'general'." },
        ],
        Risk = RiskLevel.Low,
        Capabilities = ["memory.rw"],
        Reversible = false,
        RequiresApproval = false,
    };

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        var key = args.TryGetValue("key", out var k) ? k?.ToString() : null;
        var value = args.TryGetValue("value", out var v) ? v?.ToString() : null;
        var category = args.TryGetValue("category", out var c) && !string.IsNullOrWhiteSpace(c?.ToString())
            ? c!.ToString()!
            : "general";

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return Task.FromResult(ToolResult.Fail("bad_args", "Both 'key' and 'value' are required."));

        if (!store.Enabled)
            return Task.FromResult(ToolResult.Succeed(null, "Memory is switched off, so nothing was recorded."));

        store.Remember(key, value, category);
        return Task.FromResult(ToolResult.Succeed(null, $"Remembered {key}."));
    }
}
