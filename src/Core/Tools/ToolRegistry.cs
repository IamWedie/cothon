namespace Cothon.Core.Tools;

/// <summary>
/// Name → tool lookup. Duplicate names are rejected at registration so the
/// model can never see two tools with the same name.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    public ITool? Resolve(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    public IReadOnlyCollection<ITool> All => _tools.Values;

    public void Register(ITool tool)
    {
        if (!_tools.TryAdd(tool.Definition.Name, tool))
            throw new InvalidOperationException($"Duplicate tool name: {tool.Definition.Name}");
    }
}