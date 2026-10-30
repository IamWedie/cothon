using Cothon.Core.Files;
using Cothon.Core.Tools;

namespace Cothon.Core.Phone;

/// <summary>
/// Pushes a sandbox file to the phone. The sandbox is the only allowed
/// origin: absolute paths and escapes are refused before adb ever runs,
/// and adb failures surface with their stderr — never a fake success.
/// </summary>
public sealed class PhoneTool(IPhonePusher adb, PathPolicy paths) : ITool
{
    public ToolDefinition Definition { get; } = new()
    {
        Name = "phone_push_file",
        Description = "Copy a local PC file to the phone.",
        Parameters =
        [
            new ToolParameter { Name = "local", Required = true, Description = "File inside the sandbox." },
            new ToolParameter { Name = "remote", Description = "Phone path (default /sdcard/Download/<name>)" },
        ],
        Risk = RiskLevel.High,
        Capabilities = ["phone.push", "fs.read"],
        Reversible = false,
    };

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        var local = args.TryGetValue("local", out var l) ? l as string ?? "" : "";
        var remote = args.TryGetValue("remote", out var r) && r is string rs && rs.Length > 0
            ? rs
            : "/sdcard/Download/" + Path.GetFileName(local);

        var verdict = paths.Validate(local);
        if (!verdict.Allowed)
            return ToolResult.Fail("path_denied", verdict.Reason!);
        if (!File.Exists(verdict.Canonical))
            return ToolResult.Fail("not_found", $"Local file not found: {verdict.Canonical}");

        var outcome = await adb.PushAsync(verdict.Canonical, remote, ct).ConfigureAwait(false);
        return outcome.IsFailed
            ? ToolResult.Fail("adb_failed", $"adb error: {adb.LastError}")
            : ToolResult.Succeed(new { local = verdict.Canonical, remote }, $"Pushed {verdict.Canonical} → {remote}");
    }
}
