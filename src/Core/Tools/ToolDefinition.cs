namespace Cothon.Core.Tools;

/// <summary>
/// The tool contract. Everything outside the tool body — prompt text, policy,
/// approval UI, undo — is driven from these fields, so a tool can never quietly
/// claim "harmless" behaviour it does not have.
/// </summary>
public sealed class ToolDefinition
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<ToolParameter> Parameters { get; init; } = [];
    public RiskLevel Risk { get; init; } = RiskLevel.Medium;
    /// <summary>Capability scopes the tool needs, e.g. "fs.rw", "net.api.openai".</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    /// <summary>Whether an inverse operation exists so the call can be undone.</summary>
    public bool Reversible { get; init; }
    /// <summary>Whether the broker must ask the user before executing.</summary>
    public bool RequiresApproval { get; init; }
}

/// <summary>A parameter in the tool's input schema.</summary>
public sealed class ToolParameter
{
    public required string Name { get; init; }
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
    public string Description { get; init; } = "";
    public object? Default { get; init; }
}