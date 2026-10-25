namespace Cothon.Core.Settings;

/// <summary>One external MCP server Cothon may reach for extra tools.</summary>
public sealed class McpServerConfig
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";
}
