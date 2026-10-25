using Cothon.Core.Settings;
using Cothon.Core.Tools;

namespace Cothon.Core.Mcp;

/// <summary>
/// Pulls MCP servers declared in settings into the tool registry: connect,
/// list, namespace, register — collecting progress notes, never throwing.
/// A half-connected server contributes nothing instead of breaking the app.
/// </summary>
public static class McpTools
{
    /// <summary>Real-world connect: spawns the configured process and bounds every wait.</summary>
    public static McpClient DefaultConnect(McpServerConfig server)
    {
        if (server.Command.Trim().Length == 0)
            throw new IOException("MCP server has an empty command.");
        var client = new McpClient(
            server.Name.Length > 0 ? server.Name : server.Command,
            new ProcessMcpTransport(server.Command, server.Args));
        try
        {
            client.ConnectAsync("cothon", "0.1").GetAwaiter().GetResult();
        }
        catch (AggregateException ex)
        {
            throw ex.InnerException is null ? ex : ex.InnerException;
        }
        return client;
    }

    public static IReadOnlyList<string> Register(
        ToolRegistry registry,
        IReadOnlyList<McpServerConfig> servers,
        Func<McpServerConfig, McpClient?> connect,
        Action<string> note)
    {
        var notes = new List<string>();
        foreach (var server in servers)
        {
            McpClient? client;
            try
            {
                client = connect(server);
            }
            catch (Exception ex)
            {
                note($"MCP server \"{Display(server)}\" failed to start: {Reason(ex)}");
                continue;
            }
            if (client is null)
            {
                note($"MCP server \"{Display(server)}\" is not configured to run here.");
                continue;
            }

            IReadOnlyList<McpToolInfo> tools;
            try
            {
                tools = client.ListToolsAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                note($"MCP server \"{Display(server)}\" failed to answer tools/list: {Reason(ex)}");
                continue;
            }

            var registered = 0;
            foreach (var tool in tools)
            {
                if (tool.Name.Length == 0)
                    continue;
                var adapter = new McpToolAdapter(client, tool, server.Name);
                if (registry.Resolve(adapter.Definition.Name) is not null)
                {
                    notes.Add($"\"{Display(server)}\" {tool.Name} skipped (duplicate name)");
                    note($"MCP tool \"{adapter.Definition.Name}\" skipped: duplicate name");
                    continue;
                }
                registry.Register(adapter);
                registered++;
            }
            notes.Add($"{Display(server)}: {registered} tools registered");
        }
        return notes;
    }

    private static string Display(McpServerConfig server)
        => server.Name.Length > 0 ? server.Name : server.Command;

    private static string Reason(Exception ex) =>
        ex is AggregateException { InnerException: var inner }
            ? inner?.Message ?? ex.Message
            : ex.Message;
}
