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
    /// <summary>Human-readable line describing what physically happened.</summary>
    public string Outcome { get; init; } = "";
    /// <summary>Token for undoing the effects, when the tool registered one.</summary>
    public string? UndoToken { get; init; }

    public static ToolResult Succeed(object? data, string outcome = "", string? undoToken = null) =>
        new() { Ok = true, Data = data, Outcome = outcome, UndoToken = undoToken };

    public static ToolResult Fail(string code, string message) =>
        new() { Ok = false, Data = null, Outcome = message, Error = new ToolError(code, message) };
}

public sealed record ToolError(string Code, string Message);