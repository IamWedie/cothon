namespace Cothon.Core.Undo;

/// <summary>
/// LIFO stack of inverse operations. Every reversible effect a tool creates
/// is pushed here; undo() pops and applies one entry.
/// </summary>
public sealed class UndoLedger
{
    private sealed record Entry(string ToolName, string Description, Func<Task> Undo);

    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];

    public int Count { get { lock (_gate) return _entries.Count; } }

    public void Push(string toolName, string description, Func<Task> undo)
    {
        lock (_gate) _entries.Add(new Entry(toolName, description, undo));
    }

    public string? PeekDescription()
    {
        lock (_gate) return _entries.Count == 0 ? null : _entries[^1].Description;
    }

    public async Task<string?> UndoTopAsync(CancellationToken ct = default)
    {
        Func<Task> undo;
        string description;
        lock (_gate)
        {
            if (_entries.Count == 0) return null;
            description = _entries[^1].Description;
            undo = _entries[^1].Undo;
            _entries.RemoveAt(_entries.Count - 1);
        }
        try
        {
            await undo().WaitAsync(ct).ConfigureAwait(false);
            return $"Undone: {description}";
        }
        catch (Exception ex)
        {
            return $"Undo failed: {ex.Message}";
        }
    }
}