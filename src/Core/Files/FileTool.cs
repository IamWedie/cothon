using Cothon.Core.Tools;

namespace Cothon.Core.Files;

/// <summary>
/// Sandbox-scoped file operations. Paths are validated by PathPolicy before
/// any FileSystem touch, writes return an undo entry, reads never write.
/// </summary>
public sealed class FileTool(PathPolicy paths) : ITool
{
    private string? _lastContent;

    public ToolDefinition Definition { get; } = new()
    {
        Name = "fs_write",
        Description = "Create or overwrite a text file inside the sandbox.",
        Parameters =
        [
            new ToolParameter { Name = "path", Required = true, Description = "Sandbox-relative path" },
            new ToolParameter { Name = "content", Required = true, Description = "Text to write" },
        ],
        Risk = RiskLevel.Medium,
        Capabilities = ["fs.rw"],
        Reversible = true,
        RequiresApproval = true,
    };

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        var path = args.TryGetValue("path", out var p) ? p as string : null;
        var content = args.TryGetValue("content", out var c) ? c as string : null;

        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(content))
            return ToolResult.Fail("invalid_arguments", "Both path and content are required.");

        var decision = paths.Validate(path!);
        if (!decision.Allowed)
            return ToolResult.Fail("path_denied", decision.Reason!);

        var canonical = decision.Canonical;
        var existed = File.Exists(canonical);
        var previous = existed ? await File.ReadAllTextAsync(canonical, ct).ConfigureAwait(false) : null;

        Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
        await File.WriteAllTextAsync(canonical, content!, ct).ConfigureAwait(false);

        return ToolResult.Succeed(
            new { path = canonical, bytes = content!.Length },
            existed ? $"Overwrote {path!}" : $"Created {path!}",
            undo: _ =>
            {
                if (existed) File.WriteAllText(canonical, previous!);
                else File.Delete(canonical);
                return Task.CompletedTask;
            });
    }
}