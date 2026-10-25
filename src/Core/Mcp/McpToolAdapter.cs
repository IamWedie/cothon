using System.Text.RegularExpressions;
using Cothon.Core.Tools;

namespace Cothon.Core.Mcp;

/// <summary>
/// Bridges a remote MCP tool into Cothon's registry: honest description and
/// schema straight from the server, namespaced so no server can shadow the
/// built-in tools. Not declared reversible — the server is outside our undo
/// ledger's reach.
/// </summary>
public sealed class McpToolAdapter(McpClient client, McpToolInfo tool, string serverName) : ITool
{
    private static readonly Regex Safe = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    public ToolDefinition Definition { get; } = new()
    {
        Name = $"mcp_{Safe.Replace(serverName, "_").ToLowerInvariant()}_{Safe.Replace(tool.Name, "_")}",
        Description = tool.Description.Length > 0 ? tool.Description : $"{tool.Name} (via MCP server {serverName})",
        Parameters = tool.Parameters,
        Risk = RiskLevel.Medium,
        Capabilities = [$"mcp.{Safe.Replace(serverName, "_").ToLowerInvariant()}"],
        Reversible = false,
        RequiresApproval = false,
    };

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct) =>
        await client.CallToolAsync(tool.Name, args, ct).ConfigureAwait(false);
}
