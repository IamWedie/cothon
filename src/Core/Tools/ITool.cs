namespace Cothon.Core.Tools;

/// <summary>A callable tool. Implementations live outside the registry.</summary>
public interface ITool
{
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct);
}