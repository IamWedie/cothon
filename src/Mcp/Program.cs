using Cothon.Core.Files;
using Cothon.Core.Mcp;
using Cothon.Core.Memory;
using Cothon.Core.Phone;
using Cothon.Core.Settings;
using Cothon.Core.Sessions;
using Cothon.Core.Tools;

// Cothon over MCP: any MCP client (Claude Desktop, another agent, me) drives
// Cothon's tools through this stdio process. Memory always on; sandbox +
// phone register only when their pieces exist, exactly like the app.

var settings = AppSettings.Load();
var registry = new ToolRegistry();

registry.Register(new MemoryTool(new MemoryStore(settings.MemoryLogging)));
registry.Register(new FileTool(new PathPolicy(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Cothon", "sandbox"))));

var adb = AdbProbe.ResolveAdb(settings.AdbPath,
    Environment.GetEnvironmentVariable("PATH") ?? "", AdbProbe.DefaultCommonDirs);
if (adb.Length > 0)
    registry.Register(new PhoneTool(
        new AdbClient(adb, settings.PhoneHost, settings.PhonePort),
        new PathPolicy(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cothon", "sandbox"))));

var server = new McpServer("cothon", "0.1", registry);

Console.Error.WriteLine("cothon-mcp ready: " + registry.All.Count + " tools");

var stdin = Console.In;
var buffer = new char[65536];
var lineBuffer = new System.Text.StringBuilder();

// Handshake is synchronous-friendly; notifications inside produce no reply.
while (true)
{
    var read = await stdin.ReadAsync(buffer, CancellationToken.None);
    if (read == 0)
        break;

    var chunk = buffer[..read];
    foreach (var ch in chunk)
    {
        if (ch != '\n')
        {
            lineBuffer.Append(ch);
            continue;
        }
        if (lineBuffer.Length > 0)
        {
            var response = await server.HandleLineAsync(lineBuffer.ToString(), CancellationToken.None);
            if (response is not null)
            {
                Console.Out.WriteLine(response);
                Console.Out.Flush();
            }
        }
        lineBuffer.Clear();
    }
}
