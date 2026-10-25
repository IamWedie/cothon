using System.Text.Json;
using Cothon.Core.Tools;

namespace Cothon.Core.Mcp;

/// <summary>
/// Minimal MCP server: line-delimited JSON-RPC over stdio. Speaks
/// initialize / tools/list / tools/call / ping so any MCP client can drive
/// Cothon's registered tools. Notifications produce no response line.
/// </summary>
public sealed class McpServer(string serverName, string serverVersion, ToolRegistry registry)
{
    private bool _initialized;

    public bool IsInitialized => _initialized;

    /// <summary>Handles one line and returns the response JSON, or null for notifications.</summary>
    public async Task<string?> HandleLineAsync(string line, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error: not JSON");
        }

        using var _ = doc;
        var root = doc.RootElement;

        var hasId = root.TryGetProperty("id", out var idEl);

        if (!root.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String)
            return hasId ? Error(idEl.Clone(), -32600, "Missing method") : null;
        var method = methodEl.GetString()!;

        if (!hasId)
        {
            if (method == "notifications/initialized")
                _initialized = true;
            return null;
        }

        var id = idEl.Clone();

        return method switch
        {
            "initialize" => Json(new { jsonrpc = "2.0", id, result = InitializeResult(root) }),
            "ping" => Json(new { jsonrpc = "2.0", id, result = new { } }),
            "tools/list" => Json(new { jsonrpc = "2.0", id, result = ToolsResult() }),
            "tools/call" => await CallToolAsync(id, root, ct).ConfigureAwait(false),
            _ => Error(id, -32601, $"Unknown method: {method}"),
        };
    }

    private static JsonSerializerOptions Special => new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Json(object payload) => JsonSerializer.Serialize(payload, Special);

    private static object InitializeResult(JsonElement request)
    {
        var requested = "2024-11-05";
        try
        {
            requested = request.GetProperty("params").GetProperty("protocolVersion").GetString() ?? requested;
        }
        catch (KeyNotFoundException) { }
        return new
        {
            protocolVersion = requested,
            capabilities = new { tools = new { } },
            serverInfo = new { name = "cothon", version = Version },
        };
    }

    private const string Version = "0.1";

    private object ToolsResult() => new
    {
        tools = registry.All.Select(tool => new
        {
            name = tool.Definition.Name,
            description = tool.Definition.Description,
            inputSchema = Schema(tool.Definition),
        }),
    };

    private static object Schema(ToolDefinition def)
    {
        var properties = new Dictionary<string, object?>();
        foreach (var p in def.Parameters)
            properties[p.Name] = new { type = p.Type, description = p.Description };
        var required = def.Parameters.Where(p => p.Required).Select(p => p.Name).ToList();
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required.Count > 0)
            schema["required"] = required;
        return schema;
    }

    private async Task<string> CallToolAsync(JsonElement id, JsonElement request, CancellationToken ct)
    {
        string? name = null;
        var args = new Dictionary<string, object?>();
        try
        {
            if (request.TryGetProperty("params", out var pr))
            {
                if (pr.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    name = n.GetString();
                if (pr.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
                    foreach (var prop in a.EnumerateObject())
                        args[prop.Name] = JsonToArg(prop.Value);
            }
        }
        catch (InvalidOperationException)
        {
            return Error(id, -32602, "Malformed params for tools/call");
        }

        if (string.IsNullOrWhiteSpace(name))
            return Error(id, -32602, "tools/call requires a tool name");

        var tool = registry.Resolve(name);
        if (tool is null)
            return Error(id, -32602, $"Unknown tool: {name}");

        var result = await tool.ExecuteAsync(args, ct).ConfigureAwait(false);
        var text = result.Outcome.Length > 0
            ? result.Outcome
            : (result.Ok ? "(ok)" : result.Error?.Message ?? "(failed)");
        return Json(new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                content = new[] { new { type = "text", text } },
                isError = !result.Ok,
            },
        });
    }

    private static object? JsonToArg(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var whole) ? whole : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => value.ToString(),
    };

    private static string Error(JsonElement? id, int code, string message)
    {
        var idJson = id is null ? "null" : JsonSerializer.Serialize(id, Special);
        var escaped = JsonSerializer.Serialize(message, Special);
        return """
            {"jsonrpc":"2.0","id":IDJSON,"error":{"code":CODE,"message":MSGJSON}}
            """
            .Replace("IDJSON", idJson)
            .Replace("CODE", code.ToString())
            .Replace("MSGJSON", escaped);
    }
}
