using Cothon.Core.Screen;
using Cothon.Core.Tools;

namespace Cothon.Core.Sessions;

/// <summary>
/// One user turn: ask the model, run any tool calls through the broker,
/// and return every produced message in order. Tool failures become Tool
/// messages with Ok=false — never a fake "done".
/// </summary>
public sealed class AgentLoop(IModelProvider provider, ToolBroker broker)
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
            new ModelRequest("You are Cothon.", history, userText)
            {
                Screens = screen is null ? [] : [screen],
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
}