namespace Cothon.Core.Sessions;

/// <summary>
/// Deterministic provider for tests, offline mode and first-run demos:
/// repeats the user text and optionally "asks" for a fixed tool call.
/// </summary>
public sealed class EchoModelProvider(string? toolName = null) : IModelProvider
{
    public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
    {
        var calls = toolName is null
            ? []
            : new List<ModelToolCall> { new(toolName, new Dictionary<string, object?>()) };
        return Task.FromResult(new ModelReply($"[echo] {request.UserText}", calls));
    }
}