using Cothon.Core.Undo;
using Xunit;

public class UndoLedgerTests
{
    private readonly List<string> _applied = [];

    [Fact]
    public async Task Empty_ledger_reports_nothing_to_undo()
    {
        var result = await new UndoLedger().UndoTopAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task Undone_last_first()
    {
        var ledger = new UndoLedger();
        ledger.Push("fs.write", "create notes.txt", async () => _applied.Add("undo-create"));
        ledger.Push("fs.write", "append line", async () => { await Task.Delay(1); _applied.Add("undo-append"); });

        var result = await ledger.UndoTopAsync();

        Assert.Equal("Undone: append line", result);
        Assert.Equal(["undo-append"], _applied);
        Assert.Equal("create notes.txt", ledger.PeekDescription());
    }

    [Fact]
    public async Task Push_then_undo_drains_the_ledger()
    {
        var ledger = new UndoLedger();
        ledger.Push("t", "d", () => Task.CompletedTask);
        await ledger.UndoTopAsync();

        Assert.Equal(0, ledger.Count);
        Assert.Null(await ledger.UndoTopAsync());
    }

    [Fact]
    public async Task Undo_failure_is_reported_and_entry_is_removed()
    {
        var ledger = new UndoLedger();
        ledger.Push("t", "bad op", () => throw new InvalidOperationException("disk gone"));

        var result = await ledger.UndoTopAsync();

        Assert.StartsWith("Undo failed:", result!);
        Assert.Contains("disk gone", result!);
        Assert.Equal(0, ledger.Count);
    }
}