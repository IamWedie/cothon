using Cothon.Core.Memory;
using Cothon.Core.Screen;
using Cothon.Core.Tools;

namespace Cothon.Core.Sessions;

/// <summary>
/// One user turn, real agentic style: ask the model, run its tool calls
/// through the broker, POST the outcomes back and ask again — until the
/// model answers in text (or the round cap forces a text-only reply).
/// Tool failures become Tool messages with Ok=false — never a fake "done".
/// </summary>
public sealed class AgentLoop(IModelProvider provider, ToolBroker broker, MemoryStore? memory = null)
{
    /// <summary>Tool rounds per turn before a text-only final answer is forced.</summary>
    public const int MaxToolRounds = 4;

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

        var screens = screen is null ? [] : (IReadOnlyList<ScreenFrame>)[screen];
        var steps = new List<ModelTurnStep>();

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var restrictTools = round == MaxToolRounds;
            var reply = await provider.CompleteAsync(
                new ModelRequest(MemoryPrompt(), history, userText)
                {
                    Screens = screens,
                    Tools = restrictTools ? [] : broker.Specs,
                    ToolSteps = steps,
                }, ct).ConfigureAwait(false);

            if (reply.Text.Length > 0)
                outgoing.Add(new ChatMessage(Guid.NewGuid().ToString("N"), Author.Assistant, reply.Text, DateTimeOffset.UtcNow));

            if (reply.ToolCalls.Count == 0)
                return outgoing;

            if (restrictTools)
                return outgoing; // cap reached; the text answer above is final

            var executed = new List<ModelCallExecuted>();
            foreach (var call in reply.ToolCalls)
            {
                var result = await broker.InvokeAsync(call.Name, call.Args, ct).ConfigureAwait(false);
                var text = string.IsNullOrWhiteSpace(result.Outcome)
                    ? (result.Ok ? "(no text)" : result.Error!.Message)
                    : result.Outcome;
                outgoing.Add(new ChatMessage(
                    Guid.NewGuid().ToString("N"), Author.Tool, text, DateTimeOffset.UtcNow, call.Name, result.Ok));
                executed.Add(new ModelCallExecuted(call, result.Ok, text));
            }

            steps.Add(new ModelTurnStep(reply.Text.Length > 0 ? reply.Text : "", executed));
        }

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