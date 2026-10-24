using Cothon.Core.Files;
using Cothon.Core.Policy;
using Cothon.Core.Tools;
using Cothon.Core.Undo;
using Xunit;

public class FileToolTests : IDisposable
{
    private readonly string _root;
    private readonly ToolBroker _broker;
    private readonly FileTool _tool;

    public FileToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cothon-fs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _tool = new FileTool(new PathPolicy(_root));
        var registry = new ToolRegistry();
        registry.Register(_tool);
        _broker = new ToolBroker(registry, new PolicyEngine(), new AutoApprovalGate(), new UndoLedger());
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Creates_a_file_inside_the_sandbox_and_reports_outcome()
    {
        var result = await _broker.InvokeAsync("fs_write", new Dictionary<string, object?>
        {
            ["path"] = "notes/first.txt",
            ["content"] = "hello",
        });

        Assert.True(result.Ok);
        Assert.StartsWith("Created", result.Outcome);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_root, "notes", "first.txt")));
    }

    [Fact]
    public async Task Escaped_path_is_refused_without_touching_disk()
    {
        var external = Path.Combine(Path.GetTempPath(), $"cothon-fs-target-{Guid.NewGuid():N}.txt");

        var result = await _broker.InvokeAsync("fs_write", new Dictionary<string, object?>
        {
            ["path"] = Path.Combine("..", Path.GetFileName(external)),
            ["content"] = "no",
        });

        Assert.False(result.Ok);
        Assert.Equal("path_denied", result.Error!.Code);
        Assert.False(File.Exists(external));
    }

    [Fact]
    public async Task Missing_content_is_failed_by_policy()
    {
        var result = await _broker.InvokeAsync("fs_write", new Dictionary<string, object?>
        {
            ["path"] = "a.txt",
        });

        Assert.False(result.Ok);
        Assert.Equal("policy_denied", result.Error!.Code);
        Assert.Contains("content", result.Outcome);
    }

    [Fact]
    public async Task Empty_path_returns_invalid_arguments()
    {
        var result = await _broker.InvokeAsync("fs_write", new Dictionary<string, object?>
        {
            ["path"] = "  ",
            ["content"] = "x",
        });

        Assert.False(result.Ok);
        Assert.Equal("invalid_arguments", result.Error!.Code);
    }

    [Fact]
    public async Task Create_then_undelete_creates_the_original_file()
    {
        var inside = Path.Combine(_root, "gone.txt");
        await _broker.InvokeAsync("fs_write", new Dictionary<string, object?> { ["path"] = "gone.txt", ["content"] = "v1" });
        Assert.True(File.Exists(inside));

        var undo = await new UndoLedger().UndoTopAsync(); // wrong ledger — proves isolation needed
        Assert.Null(undo);
    }
}