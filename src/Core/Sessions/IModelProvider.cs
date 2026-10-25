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

    /// <summary>Tool round-trips already executed this turn, newest last, so the model can answer after tool runs.</summary>
    public IReadOnlyList<ModelTurnStep> ToolSteps { get; init; } = [];
}

/// <summary>A single model→tools→results round inside one user turn.</summary>
/// <param name="AssistantText">What the model said before/while calling (may be empty).</param>
/// <param name="Calls">Executed calls with their outcomes.</param>
public sealed record ModelTurnStep(
    string AssistantText,
    IReadOnlyList<ModelCallExecuted> Calls);

public sealed record ModelCallExecuted(ModelToolCall Call, bool Ok, string Outcome);

public sealed record ToolSpec(string Name, string Description);

public sealed record ModelReply(
    string Text,
    IReadOnlyList<ModelToolCall> ToolCalls);

public sealed record ModelToolCall(
    string Name,
    IReadOnlyDictionary<string, object?> Args)
{
    /// <summary>Provider call id (OpenAI/Anthropic); empty for Gemini.</summary>
    public string Id { get; init; } = "";
}

/// <summary>A model runtime. Fake in tests, Gemini/OpenAI/local in production.</summary>
public interface IModelProvider
{
    Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default);
}