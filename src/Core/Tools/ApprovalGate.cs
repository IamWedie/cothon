namespace Cothon.Core.Tools;

/// <summary>A pending dangerous action the user must decide on.</summary>
public sealed record ApprovalRequest(string ToolName, ToolDefinition Definition, IReadOnlyDictionary<string, object?> Args);

public interface IApprovalGate
{
    /// <summary>Shows the effect preview and waits. True = user allowed it.</summary>
    Task<bool> RequestAsync(ApprovalRequest request, CancellationToken ct = default);
}

/// <summary>Auto-approver for tests and headless mode. Never the user-facing gate.</summary>
public sealed class AutoApprovalGate : IApprovalGate
{
    public Task<bool> RequestAsync(ApprovalRequest request, CancellationToken ct = default) => Task.FromResult(true);
}

public sealed class DenyApprovalGate : IApprovalGate
{
    public Task<bool> RequestAsync(ApprovalRequest request, CancellationToken ct = default) => Task.FromResult(false);
}