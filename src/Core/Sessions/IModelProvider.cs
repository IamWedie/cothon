using Cothon.Core.Screen;

namespace Cothon.Core.Sessions;

/// <summary>What the agent loop asks the model for and what comes back.</summary>
/// <param name="Screens">Optional frames (e.g. the user's screen) read this turn.</param>
public sealed record ModelRequest(
    string SystemPrompt,
    IReadOnlyList<ChatMessage> History,
    string UserText)
{
    public IReadOnlyList<ToolSpec>? Tools { get; init; }
    public IReadOnlyList<ScreenFrame> Screens { get; init; } = [];
}

public sealed record ToolSpec(string Name, string Description);

public sealed record ModelReply(
    string Text,
    IReadOnlyList<ModelToolCall> ToolCalls);

public sealed record ModelToolCall(string Name, IReadOnlyDictionary<string, object?> Args);

/// <summary>A model runtime. Fake in tests, Gemini/OpenAI/local in production.</summary>
public interface IModelProvider
{
    Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default);
}