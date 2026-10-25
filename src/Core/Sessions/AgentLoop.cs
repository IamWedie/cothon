using Cothon.Core.Memory;
using Cothon.Core.Screen;
using Cothon.Core.Tools;

namespace Cothon.Core.Sessions;

/// <summary>
/// One user turn: ask the model, run any tool calls through the broker,
/// and return every produced message in order. Tool failures become Tool
/// messages with Ok=false — never a fake "done". Durable user memory,
/// when enabled, is injected into every system prompt.
/// </summary>
public sealed class AgentLoop(IModelProvider provider, ToolBroker broker, MemoryStore? memory = null)
{
    public async Task<IReadOnlyList<ChatMessage>> RunAsync(
        IReadOnlyList<ChatMessage> history, string userText,
        CancellationToken ct = default)
        => await RunAsync(history, userText, screen: null, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<ChatMessage>> RunAsync(
        IReadOnlyList<ChatMessage> history, string userText,
        ScreenFrame? screen, CancellationToken ct = default)
    {
        var user = new ChatMessage(Guid.NewGuid().ToString("N"), Author.User, userText, DateTimeOffset.UtcNow);
        var outgoing = new List<ChatMessage> { user };

        var reply = await provider.CompleteAsync(
            new ModelRequest(MemoryPrompt(), history, userText)
            {
                Screens = screen is null ? [] : [screen],
                Tools = broker.Specs,
            }, ct).ConfigureAwait(false);

        foreach (var call in reply.ToolCalls)
        {
            var result = await broker.InvokeAsync(call.Name, call.Args, ct).ConfigureAwait(false);
            outgoing.Add(new ChatMessage(
                Guid.NewGuid().ToString("N"), Author.Tool,
                string.IsNullOrWhiteSpace(result.Outcome) ? (result.Ok ? "(no text)" : result.Error!.Message) : result.Outcome,
                DateTimeOffset.UtcNow, call.Name, result.Ok));
        }

        outgoing.Add(new ChatMessage(Guid.NewGuid().ToString("N"), Author.Assistant, reply.Text, DateTimeOffset.UtcNow));
        return outgoing;
    }

    private string MemoryPrompt()
    {
        var prompt = new System.Text.StringBuilder("You are Cothon.");
        if (memory is null || !memory.Enabled)
            return prompt.ToString();

        var facts = memory.Recall();
        if (facts.Count == 0)
            return prompt.ToString();

        prompt.AppendLine();
        prompt.AppendLine("Durable facts you know about the user (use naturally; do not recite):");
        foreach (var fact in facts.Take(40))
            prompt.AppendLine($"- {fact.Key}: {fact.Value} ({fact.Category})");
        return prompt.ToString();
    }
}