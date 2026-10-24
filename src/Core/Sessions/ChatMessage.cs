namespace Cothon.Core.Sessions;

public enum Author { User, Assistant, Tool, System }

public sealed record ChatMessage(
    string Id,
    Author Author,
    string Text,
    DateTimeOffset At,
    string? ToolName = null,
    bool Ok = true);