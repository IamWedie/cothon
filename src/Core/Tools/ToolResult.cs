namespace Cothon.Core.Tools;

/// <summary>
/// Structured result of a tool call. No tool may answer with free-form "done":
/// success is data, failure always carries a machine-readable code.
/// </summary>
public sealed class ToolResult
{
    public bool Ok { get; init; }
    public object? Data { get; init; }
    public ToolError? Error { get; init; }
    public string Outcome { get; init; } = "";
    /// <summary>Async inverse operation, invoked by the broker to undo the effect.</summary>
    public Func<IReadOnlyDictionary<string, object?>, Task>? UndoFollower { get; init; }

    public static ToolResult Succeed(object? data, string outcome = "", Func<IReadOnlyDictionary<string, object?>, Task>? undo = null) =>
        new() { Ok = true, Data = data, Outcome = outcome, UndoFollower = undo };

    public static ToolResult Fail(string code, string message) =>
        new() { Ok = false, Outcome = message, Error = new ToolError(code, message) };
}

public sealed record ToolError(string Code, string Message);