using Cothon.Core.Memory;
using Cothon.Core.Mcp;
using Cothon.Core.Sessions;
using Cothon.Core.Tools;
using Xunit;

public class McpServerTests : IDisposable
{
    private readonly string _memoryPath = Path.Combine(Path.GetTempPath(), $"cothon-mcp-{Guid.NewGuid():N}.json");
    private readonly ToolRegistry _registry = new();
    private readonly McpServer _server;

    public McpServerTests()
    {
        _registry.Register(new MemoryTool(new MemoryStore(enabled: true, path: _memoryPath)));
        _server = new McpServer("cothon", "0.1", _registry);
    }

    [Fact]
    public async Task Initialize_handshakes_with_capabilities()
    {
        var response = await _server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""");

        Assert.NotNull(response);
        Assert.Contains("\"serverInfo\"", response);
        Assert.Contains("\"tools\"", response);
        Assert.Contains("\"name\":\"cothon\"", response.Replace(" ", "").Replace("\n", "")); // name surfaces anywhere
    }

    [Fact]
    public void Initialized_notification_ends_the_handshake_without_reply()
    {
        var response = _server.HandleLineAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""").Result;

        Assert.Null(response);
        Assert.True(_server.IsInitialized);
    }

    [Fact]
    public async Task Tools_list_declares_the_registry_with_schemas()
    {
        await _server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");

        var response = await _server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        Assert.NotNull(response);
        Assert.Contains("memory_save", response);
        Assert.Contains("\"inputSchema\"", response);
        Assert.Contains("durable fact", response.Replace("\n", ""));
    }

    [Fact]
    public async Task Tools_call_invokes_a_registered_tool_and_persists()
    {
        await _server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");

        var response = await _server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"memory_save","arguments":{"key":"coffee","value":"oat"}}}""");

        Assert.NotNull(response);
        Assert.Contains("\"isError\":false", response.Replace(", ", ","));
        Assert.Contains("Remembered coffee", response);
        // the fact physically landed in memory.json
        var raw = await File.ReadAllTextAsync(_memoryPath);
        Assert.Contains("oat", raw);
    }

    [Fact]
    public async Task Unknown_tool_call_is_a_clean_protocol_error()
    {
        var response = await _server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"missing","arguments":{}}}""");

        Assert.Contains("Unknown tool: missing", response!);
        Assert.Contains("-32602", response);
        Assert.Contains("\"id\":7", response);
    }

    [Fact]
    public async Task Bad_json_gets_a_parse_error_with_null_id()
    {
        var response = await _server.HandleLineAsync("(((not json");

        Assert.Contains("\"id\":null", response!);
        Assert.Contains("-32700", response);
    }

    [Fact]
    public async Task Ping_answers_an_empty_result()
    {
        var response = await _server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":"abc","method":"ping"}""");

        Assert.Contains("\"id\":\"abc\"", response);
    }

    [Fact]
    public async Task Echoed_id_keeps_its_original_type()
    {
        var response = await _server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":"client-7","method":"tools/list"}""");

        Assert.Contains("\"id\":\"client-7\"", response);
    }

    public void Dispose()
    {
        if (File.Exists(_memoryPath))
            File.Delete(_memoryPath);
    }
}
