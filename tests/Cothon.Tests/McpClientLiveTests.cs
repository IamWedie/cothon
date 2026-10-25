using Cothon.Core.Memory;
using Cothon.Core.Mcp;

namespace Cothon.Tests;

/// <summary>
/// Ends-to-end against the shipped Cothon MCP host over a REAL process:
/// only runs when the host project has been built next to the repo root,
/// so routine `dotnet test` keeps its feet dry.
/// </summary>
public class McpClientLiveTests
{
    [Fact]
    public async Task Cothon_mcp_host_answers_over_a_real_process()
    {
        var exe = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Mcp", "bin", "Debug", "net10.0", "Cothon.Mcp.exe"));
        if (!File.Exists(exe))
            return; // host not built in this run — the smoke is exercised by the repo build

        var client = new McpClient("cothon-host", new ProcessMcpTransport(exe, ""), timeoutMs: 20000);
        await client.ConnectAsync("cothon", "0.1");
        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "memory_save");

        var save = await client.CallToolAsync("memory_save", new Dictionary<string, object?>
        {
            ["key"] = "mcp_live_probe",
            ["value"] = "worked",
        });
        Assert.True(save.Ok, save.Outcome);

        var store = new MemoryStore(true);
        Assert.Contains(
            store.Recall(),
            f => f.Key == "mcp_live_probe");
        store.Forget("mcp_live_probe");
    }
}
