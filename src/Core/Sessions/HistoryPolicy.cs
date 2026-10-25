namespace Cothon.Core.Sessions;

/// <summary>
/// Keeps the model's view of the conversation small and recent:
/// newest messages survive whole, older ones fall out whole —
/// never half a message, never a reversed window.
/// </summary>
public sealed class HistoryPolicy(int maxChars = 6000, int maxMessages = 40)
{
    public IReadOnlyList<ChatMessage> Trim(IReadOnlyList<ChatMessage> history)
    {
        var kept = new List<ChatMessage>();
        var chars = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var size = history[i].Text.Length;
            if (kept.Count > 0 && (chars + size > maxChars || kept.Count >= maxMessages))
                break;
            kept.Add(history[i]);
            chars += size;
        }
        kept.Reverse();
        return kept;
    }
}
