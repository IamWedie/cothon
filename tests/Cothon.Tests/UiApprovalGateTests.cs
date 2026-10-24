using Cothon.Core.Tools;
using Cothon.Ui.ViewModels;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// The in-app approval moment: a pending card, three answers, and the
/// "always allow this tool for this session" memory.
/// </summary>
public sealed class UiApprovalGateTests
{
    private static ApprovalRequest SampleRequest(string name = "fs_write") => new(
        ToolName: name,
        Definition: new ToolDefinition
        {
            Name = name,
            Description = "writes a file",
            Reversible = true,
            RequiresApproval = true,
        },
        Args: new Dictionary<string, object?>
        {
            ["path"] = "notes/today.md",
            ["text"] = "hi",
        });

    [Fact]
    public async Task Deny_clears_pending_and_returns_false()
    {
        var vm = new MainWindowViewModel();
        var pending = vm.RequestApprovalAsync(SampleRequest());

        Assert.NotNull(vm.PendingApproval);
        Assert.True(vm.HasPendingApproval);

        vm.DenyApproval();

        Assert.False(await pending);
        Assert.Null(vm.PendingApproval);
        Assert.False(vm.HasPendingApproval);
    }

    [Fact]
    public async Task Approve_once_does_not_remember()
    {
        var vm = new MainWindowViewModel();

        var first = vm.RequestApprovalAsync(SampleRequest());
        vm.ApproveOnce();
        Assert.True(await first);

        var second = vm.RequestApprovalAsync(SampleRequest());
        Assert.NotNull(vm.PendingApproval); // asked again
        vm.DenyApproval();
        Assert.False(await second);
    }

    [Fact]
    public async Task Approve_always_remembers_for_same_tool_only()
    {
        var vm = new MainWindowViewModel();

        var first = vm.RequestApprovalAsync(SampleRequest());
        vm.ApproveAlways();
        Assert.True(await first);

        var same = await vm.RequestApprovalAsync(SampleRequest());
        Assert.True(same);
        Assert.Null(vm.PendingApproval); // silent skip

        var other = vm.RequestApprovalAsync(SampleRequest("fs_delete"));
        Assert.NotNull(vm.PendingApproval); // another tool is a new conversation
        vm.DenyApproval();
        Assert.False(await other);
    }
}
