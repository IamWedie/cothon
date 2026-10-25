using Cothon.Core.Sessions;

namespace Cothon.Tests;

public class HistoryPolicyTests
{
    private static ChatMessage Msg(string text, Author author = Author.User) =>
        new(Guid.NewGuid().ToString("N"), author, text, DateTimeOffset.UtcNow);

    [Fact]
    public void Under_budget_history_passes_through_untouched()
    {
        var policy = new HistoryPolicy(maxChars: 1000, maxMessages: 10);
        var history = new List<ChatMessage> { Msg("a"), Msg("b"), Msg("c") };

        var trimmed = policy.Trim(history);

        Assert.Equal(3, trimmed.Count);
        Assert.Equal(["a", "b", "c"], trimmed.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Trimming_keeps_the_newest_messages_whole()
    {
        var policy = new HistoryPolicy(maxChars: 20, maxMessages: 100);
        var history = new List<ChatMessage>
        {
            Msg("0123456789"),
            Msg("0123456789"),
            Msg("0123456789"),
            Msg("tail"),
        };

        var trimmed = policy.Trim(history);

        var texts = trimmed.Select(m => m.Text).ToArray();
        Assert.Equal(2, trimmed.Count);           // tail + one body; the next body would breach 20 chars
        Assert.Equal(["0123456789", "tail"], texts);
    }

    [Fact]
    public void The_newest_message_survives_even_if_oversized()
    {
        var policy = new HistoryPolicy(maxChars: 5, maxMessages: 100);
        var giant = new string('x', 5000);
        var history = new List<ChatMessage> { Msg("old"), Msg(giant) };

        var trimmed = policy.Trim(history);

        var msg = Assert.Single(trimmed);
        Assert.Equal(giant, msg.Text);
    }

    [Fact]
    public void Message_count_cap_trims_first()
    {
        var policy = new HistoryPolicy(maxChars: 10000, maxMessages: 2);
        var history = new List<ChatMessage> { Msg("a"), Msg("b"), Msg("c"), Msg("d") };

        var trimmed = policy.Trim(history);

        Assert.Equal(2, trimmed.Count);
        Assert.Equal(["c", "d"], trimmed.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void Empty_history_yields_empty()
    {
        Assert.Empty(new HistoryPolicy().Trim([]));
    }
}
