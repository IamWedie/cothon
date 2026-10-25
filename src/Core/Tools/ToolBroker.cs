using Cothon.Core.Policy;
using Cothon.Core.Sessions;
using Cothon.Core.Undo;

namespace Cothon.Core.Tools;

/// <summary>
/// The single execution path for every tool call: policy → approval → run →
/// undo registration. Tools cannot be invoked without passing through here,
/// so a decision to skip policy or approval is always a code change, never a
/// forgotten call site.
/// </summary>
public sealed class ToolBroker
{
    private readonly ToolRegistry _registry;
    private readonly IRiskPolicy _policy;
    private readonly IApprovalGate _approval;
    private readonly UndoLedger _undo;

    public ToolBroker(ToolRegistry registry, IRiskPolicy policy, IApprovalGate approval, UndoLedger undo)
    {
        _registry = registry;
        _policy = policy;
        _approval = approval;
        _undo = undo;
    }

    /// <summary>What the model sees as callable; descriptions drive the prompt surface.</summary>
    public IReadOnlyList<ToolSpec> Specs => [.. _registry.All.Select(SpecOf)];

    private static ToolSpec SpecOf(ITool tool)
    {
        var segments = new List<string>();
        if (tool.Definition.Description.Length > 0)
            segments.Add(tool.Definition.Description);
        if (tool.Definition.Parameters.Count > 0)
            segments.Add(string.Join("; ", tool.Definition.Parameters.Select(p =>
                $"{p.Name}{(p.Required ? "*" : "")}: {p.Description}")));
        return new ToolSpec(tool.Definition.Name, string.Join(" | ", segments));
    }

    public async Task<ToolResult> InvokeAsync(string toolName, IReadOnlyDictionary<string, object?>? args = null, CancellationToken ct = default)
    {
        args ??= new Dictionary<string, object?>();

        var tool = _registry.Resolve(toolName);
        if (tool is null)
            return ToolResult.Fail("unknown_tool", $"Unknown tool: {toolName}");

        var decision = _policy.Evaluate(tool.Definition, args);
        if (!decision.Allowed)
            return ToolResult.Fail("policy_denied", decision.DenyReason ?? "Denied by policy.");

        if (tool.Definition.RequiresApproval)
        {
            var approved = await _approval.RequestAsync(
                new ApprovalRequest(tool.Definition.Name, tool.Definition, args), ct).ConfigureAwait(false);
            if (!approved)
                return ToolResult.Fail("approval_refused", "The user refused this action.");
        }

        try
        {
            var result = await tool.ExecuteAsync(args, ct).ConfigureAwait(false);
            if (result.Ok && tool.Definition.Reversible && result.UndoFollower is not null)
                _undo.Push(toolName, result.Outcome, () => result.UndoFollower!(new Dictionary<string, object?>()));
            return result;
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail("cancelled", "The call was cancelled.");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail("tool_failure", $"{toolName} failed: {ex.Message}");
        }
    }
}