using System.Text.Json;
using Cothon.Core.Tools;

namespace Cothon.Core.Mcp;

/// <summary>Line transport to a remote MCP server (stdio of a spawned process).</summary>
public interface IMcpTransport
{
    Task StartAsync();
    void Send(string line);
    Task<string?> ReadLineAsync();
    Task StopAsync();
}

public sealed class McpProtocolException : IOException
{
    public int Code { get; }

    public McpProtocolException(int code, string message)
        : base($"MCP protocol error {code}: {message}")
        => Code = code;
}

public sealed record McpToolInfo(string Name, string Description, JsonElement InputSchema)
{
    public IReadOnlyList<ToolParameter> Parameters => ParametersFrom(InputSchema);

    private static IReadOnlyList<ToolParameter> ParametersFrom(JsonElement schema)
    {
        var list = new List<ToolParameter>();
        if (schema.ValueKind != JsonValueKind.Object)
            return list;
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return list;
        foreach (var prop in properties.EnumerateObject())
        {
            var type = prop.Value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "string" : "string";
            var description = prop.Value.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() ?? "" : "";
            var required = schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array
                && req.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == prop.Name);
            list.Add(new ToolParameter
            {
                Name = prop.Name,
                Type = type,
                Description = description,
                Required = required,
            });
        }
        return list;
    }
}

/// <summary>
/// Client half of the MCP dialect Cothon itself serves, mirrored: single-flight
/// requests, notifications skipped while awaiting answers, honest protocol
/// failures — never a confabulated "the server said yes".
/// </summary>
public sealed class McpClient(string serverName, IMcpTransport transport, int timeoutMs = 15000)
{
    private int _id;

    public async Task ConnectAsync(string clientName, string clientVersion, CancellationToken ct = default)
    {
        await transport.StartAsync().ConfigureAwait(false);
        var line = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = ++_id,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = clientName, version = clientVersion },
                },
            });
        transport.Send(line);
        await ReadResultAsync(ct).ConfigureAwait(false);
        transport.Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
    }

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct = default)
    {
        transport.Send(System.Text.Json.JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id = ++_id, method = "tools/list", @params = new { } }));
        var response = await ReadResultAsync(ct).ConfigureAwait(false);

        var tools = new List<McpToolInfo>();
        if (response.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                var name = entry.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? "" : "";
                if (name.Length == 0)
                    continue;
                var description = entry.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString() ?? "" : "";
                var schema = entry.TryGetProperty("inputSchema", out var s) && s.ValueKind == JsonValueKind.Object
                    ? s.Clone() : default;
                tools.Add(new McpToolInfo(name, description, schema));
            }
        }
        return tools;
    }

    public async Task<ToolResult> CallToolAsync(
        string name, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        transport.Send(System.Text.Json.JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = ++_id,
            method = "tools/call",
            @params = new { name, arguments = args },
        }));
        var response = await ReadResultAsync(ct).ConfigureAwait(false);

        var text = "";
        if (response.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            && content.GetArrayLength() > 0
            && content[0].TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            text = t.GetString() ?? "";
        var isError = response.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;

        if (isError)
            return ToolResult.Fail("tool_error", text.Length > 0 ? text : "tool reported failure with no detail.");
        return text.Length > 0 ? ToolResult.Succeed(text, text) : ToolResult.Succeed(text, "(no text)");
    }

    /// <summary>Reads forward until the reply to the last sent id; notifications are skipped.</summary>
    private async Task<JsonElement> ReadResultAsync(CancellationToken ct)
    {
        while (true)
        {
            var readTask = transport.ReadLineAsync();
            if (await Task.WhenAny(readTask, Task.Delay(timeoutMs, ct)).ConfigureAwait(false) != readTask)
            {
                try { await transport.StopAsync().ConfigureAwait(false); } catch { }
                throw new IOException($"MCP server '{serverName}' did not answer within {timeoutMs} ms (awaiting reply to id {_id}).");
            }
            var line = await readTask.ConfigureAwait(false);
            if (line is null)
            {
                try { await transport.StopAsync().ConfigureAwait(false); } catch { /* stopping a dead transport is free */ }
                throw new IOException($"MCP server '{serverName}' closed the pipe or stopped answering (awaiting reply to id {_id}).");
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue; // transport noise: not a JSON-RPC frame
            }
            using var _ = doc;
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind == JsonValueKind.Null)
                continue; // a notification; keep waiting

            if (idEl.GetInt32() != _id)
                continue; // stale or unsolicited; keep waiting

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                var code2 = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                    ? c.GetInt32() : 0;
                var message = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString() ?? "" : "protocol error with no message";
                throw new McpProtocolException(code2, message);
            }

            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
    }
}
