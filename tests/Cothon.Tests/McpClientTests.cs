using Cothon.Core.Mcp;
using Cothon.Core.Tools;
using Xunit;

namespace Cothon.Tests;

public class McpClientTests
{
    private sealed class FakeTransport : IMcpTransport
    {
        public readonly List<string> Sent = [];
        public readonly Queue<string> Incoming = new();
        public bool Started;
        public bool Stopped;

        public Task StartAsync() { Started = true; return Task.CompletedTask; }
        public void Send(string line) => Sent.Add(line);
        public Task<string?> ReadLineAsync() => Task.FromResult<string?>(Incoming.Count > 0 ? Incoming.Dequeue() : null);
        public Task StopAsync() { Stopped = true; return Task.CompletedTask; }
    }

    private static FakeTransport TransportWith(params string[] responses)
    {
        var t = new FakeTransport();
        foreach (var r in responses) t.Incoming.Enqueue(r);
        return t;
    }

    private const string InitOk =
        """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05","capabilities":{},"serverInfo":{"name":"notes","version":"1.0"}}}""";

    private const string ToolsOk =
        """
        {"jsonrpc":"2.0","id":2,"result":{"tools":[
          {"name":"add_note","description":"Append a note",
           "inputSchema":{"type":"object","properties":{"text":{"type":"string","description":"Note body"}},"required":["text"]}},
          {"name":"ping_me","description":"","inputSchema":{"type":"object"}}
        ]}}
        """;

    private const string CallOk =
        """{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"noted yes"}],"isError":false}}""";

    private const string CallFailed =
        """{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"store is sealed"}],"isError":true}}""";

    private const string CallOkAfterListing =
        """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"noted yes"}],"isError":false}}""";

    private static async Task<McpClient> ConnectedAsync(FakeTransport transport)
    {
        var client = new McpClient("notes", transport);
        await client.ConnectAsync("cothon", "0.1");
        return client;
    }

    [Fact]
    public async Task Connect_handshakes_then_announces_initialized()
    {
        var t = TransportWith(InitOk);
        await ConnectedAsync(t);

        Assert.Contains("\"method\":\"initialize\"", t.Sent[0]);
        Assert.Contains("\"clientInfo\"", t.Sent[0]);
        Assert.Contains("\"method\":\"notifications/initialized\"", t.Sent[1]);
    }

    [Fact]
    public async Task Notification_lines_between_responses_are_skipped()
    {
        var t = TransportWith("""{"jsonrpc":"2.0","method":"notifications/progress","params":{"done":1}}""", InitOk, ToolsOk);
        var client = await ConnectedAsync(t);

        var tools = await client.ListToolsAsync();

        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public async Task Tools_list_becomes_infos_with_schema()
    {
        var t = TransportWith(InitOk, ToolsOk);
        var client = await ConnectedAsync(t);

        var tools = (await client.ListToolsAsync()).ToList();

        Assert.Equal("add_note", tools[0].Name);
        Assert.Equal("Append a note", tools[0].Description);
        var textParam = Assert.Single(tools[0].Parameters);
        Assert.Equal("text", textParam.Name);
        Assert.True(textParam.Required);
        Assert.False(tools[1].Parameters.Count > 0); // schemaless tool
    }

    [Fact]
    public async Task Call_tool_success_speaks_text()
    {
        var t = TransportWith(InitOk, CallOk);
        var client = await ConnectedAsync(t);

        var result = await client.CallToolAsync("add_note", new Dictionary<string, object?> { ["text"] = "hello" });

        Assert.True(result.Ok);
        Assert.Equal("noted yes", result.Outcome);
        Assert.Contains("\"arguments\":{\"text\":\"hello\"}", t.Sent[^1]);
    }

    [Fact]
    public async Task Call_tool_isError_is_a_failed_result()
    {
        var t = TransportWith(InitOk, CallFailed);
        var client = await ConnectedAsync(t);

        var result = await client.CallToolAsync("add_note", new Dictionary<string, object?>());

        Assert.False(result.Ok);
        Assert.Equal("tool_error", result.Error!.Code);
        Assert.Equal("store is sealed", result.Outcome);
    }

    [Fact]
    public async Task Server_protocol_error_is_reported_honestly()
    {
        var t = TransportWith(InitOk, """{"jsonrpc":"2.0","id":2,"error":{"code":-32601,"message":"no such method"}}""");
        var client = await ConnectedAsync(t);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync("x", new Dictionary<string, object?>()));

        Assert.Equal(-32601, ex.Code);
        Assert.Contains("no such method", ex.Message);
    }

    [Fact]
    public async Task Transport_without_answers_times_out_cleanly()
    {
        var t = TransportWith(); // reads null forever
        var client = new McpClient("silent", t);

        var ex = await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync("cothon", "0.1"));

        Assert.Contains("silent", ex.Message);
        Assert.True(t.Stopped);
    }

    private sealed class FrozenTransport : IMcpTransport
    {
        public bool Stopped;

        public Task StartAsync() => Task.CompletedTask;
        public void Send(string line) { }
        public Task<string?> ReadLineAsync() => new TaskCompletionSource<string?>().Task;
        public Task StopAsync() { Stopped = true; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Frozen_server_hits_the_deadline_and_is_stopped()
    {
        var t = new FrozenTransport();
        var client = new McpClient("frozen", t, timeoutMs: 250);

        var ex = await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync("cothon", "0.1"));

        Assert.Contains("did not answer within 250 ms", ex.Message);
        Assert.True(t.Stopped);
    }

    [Fact]
    public async Task Adapter_wraps_a_tool_into_the_registry_contract()
    {
        var t = TransportWith(InitOk, ToolsOk);
        var client = await ConnectedAsync(t);
        var tools = await client.ListToolsAsync();

        var adapter = new McpToolAdapter(client, tools[0], serverName: "notes-srv");

        Assert.Equal("mcp_notes_srv_add_note", adapter.Definition.Name);
        Assert.Equal("Append a note", adapter.Definition.Description);
        var param = Assert.Single(adapter.Definition.Parameters);
        Assert.True(param.Required);
        Assert.Equal(RiskLevel.Medium, adapter.Definition.Risk);
        Assert.Equal(["mcp.notes_srv"], adapter.Definition.Capabilities);
        Assert.False(adapter.Definition.Reversible);
    }

    [Fact]
    public async Task Adapter_execute_sends_args_and_wraps_outcome()
    {
        var t = TransportWith(InitOk, ToolsOk, CallOkAfterListing);
        var client = await ConnectedAsync(t);
        var tools = await client.ListToolsAsync();
        var adapter = new McpToolAdapter(client, tools[0], "notes");

        var result = await adapter.ExecuteAsync(new Dictionary<string, object?> { ["text"] = "pin the window" }, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("noted yes", result.Outcome);
        Assert.Contains("\"name\":\"add_note\"", t.Sent[^1]);
        Assert.Contains("\"arguments\":{\"text\":\"pin the window\"}", t.Sent[^1]);
    }

    [Fact]
    public async Task Register_namespaces_tools_and_skips_duplicates()
    {
        var registry = new ToolRegistry();
        var c1 = await ConnectedAsync(TransportWith(InitOk, ToolsOk));
        var c2 = await ConnectedAsync(TransportWith(InitOk, ToolsOk));
        var clients = new Dictionary<string, Queue<McpClient>>
        {
            ["notes"] = new([c1, c2]),
        };

        var notes = McpTools.Register(
            registry,
            [new Cothon.Core.Settings.McpServerConfig { Name = "notes" }],
            cfg => clients.TryGetValue(cfg.Name, out var q) && q.TryDequeue(out var c) ? c : null,
            _ => { });

        Assert.Equal(["notes: 2 tools registered"], notes);
        Assert.NotNull(registry.Resolve("mcp_notes_add_note"));

        var again = McpTools.Register(
            registry,
            [new Cothon.Core.Settings.McpServerConfig { Name = "notes" }],
            cfg => clients.TryGetValue(cfg.Name, out var q) && q.TryDequeue(out var c) ? c : null,
            _ => { });

        Assert.Equal(3, again.Count);
        Assert.Contains("skipped", again[0]);
        Assert.EndsWith("notes: 0 tools registered", again[^1]);
    }
}
