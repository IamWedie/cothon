using Cothon.Core.Tools;

namespace Cothon.Core.Policy;

/// <summary>Decides, before execution, whether a tool call may run at all.</summary>
public interface IRiskPolicy
{
    PolicyDecision Evaluate(ToolDefinition tool, IReadOnlyDictionary<string, object?> args);
}