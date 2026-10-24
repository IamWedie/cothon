using Cothon.Core.Tools;

namespace Cothon.Core.Policy;

/// <summary>
/// Conservative default policy: unknown-shaped arguments are data, tools carry
/// their own risk, and calls that are not explicitly allowed are denied.
/// </summary>
public sealed class PolicyEngine : IRiskPolicy
{
    public PolicyDecision Evaluate(ToolDefinition tool, IReadOnlyDictionary<string, object?> args)
    {
        var missing = tool.Parameters.Where(p => p.Required && !args.ContainsKey(p.Name)).Select(p => p.Name).ToList();
        if (missing.Count > 0)
            return PolicyDecision.Deny($"Missing argument(s): {string.Join(", ", missing)}");

        var unknown = args.Keys.Where(
            k => tool.Parameters.Any() && tool.Parameters.All(p => p.Name != k)).ToList();
        if (unknown.Count > 0)
            return PolicyDecision.Deny($"Unknown argument(s): {string.Join(", ", unknown)}");

        return PolicyDecision.Allow();
    }
}