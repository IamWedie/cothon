using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cothon.Ui.ViewModels;

/// <summary>Timeline entries render differently per message role.</summary>
public abstract record TimelineItem
{
    public abstract string Label { get; }
    public abstract string Text { get; }
    public abstract bool Ok { get; }
}

public sealed record UserItem : TimelineItem
{
    public UserItem(string text) => Text = text;
    public override string Label => "you";
    public override string Text { get; }
    public override bool Ok => true;
}

public sealed record AssistantItem : TimelineItem
{
    public AssistantItem(string text) => Text = text;
    public override string Label => "cothon";
    public override string Text { get; }
    public override bool Ok => true;
}

public sealed record ToolItem : TimelineItem
{
    public ToolItem(string toolName, string text, bool ok)
    {
        ToolName = toolName;
        Text = text;
        Ok = ok;
    }

    public string ToolName { get; }
    public override string Label => $"tool: {ToolName}";
    public override string Text { get; }
    public override bool Ok { get; }
}

public sealed record SystemItem : TimelineItem
{
    public SystemItem(string text) => Text = text;
    public override string Label => "cothon";
    public override string Text { get; }
    public override bool Ok => true;
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly Core.Sessions.AgentLoop _loop;
    private readonly List<Core.Sessions.ChatMessage> _history = [];

    [ObservableProperty]
    private string _presence = "Ready";

    [ObservableProperty]
    private bool _isThinking;

    [ObservableProperty]
    private string _input = "";

    [ObservableProperty]
    private bool _hasMessages;

    public ObservableCollection<TimelineItem> Timeline { get; } = [];

    public MainWindowViewModel()
        : this(BuildDefaultLoop())
    {
    }

    public MainWindowViewModel(Core.Sessions.AgentLoop loop)
    {
        _loop = loop;
    }

    private static Core.Sessions.AgentLoop BuildDefaultLoop()
    {
        var registry = new Core.Tools.ToolRegistry();
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cothon", "sandbox");
        registry.Register(new Core.Files.FileTool(new Core.Files.PathPolicy(root)));
        return new Core.Sessions.AgentLoop(
            new Core.Sessions.EchoModelProvider(),
            new Core.Tools.ToolBroker(
                registry, new Core.Policy.PolicyEngine(), new UserApprovalGate(), new Core.Undo.UndoLedger()));
    }

    [RelayCommand]
    private async Task SendAsync(CancellationToken ct)
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsThinking) return;
        Input = "";
        HasMessages = true;
        IsThinking = true;
        Presence = "Thinking…";
        try
        {
            var messages = await _loop.RunAsync(_history, text, ct);
            foreach (var m in messages)
            {
                switch (m.Author)
                {
                    case Core.Sessions.Author.User:
                        Timeline.Add(new UserItem(m.Text));
                        break;
                    case Core.Sessions.Author.Assistant:
                        Timeline.Add(new AssistantItem(m.Text));
                        break;
                    case Core.Sessions.Author.Tool:
                        Timeline.Add(new ToolItem(m.ToolName ?? "tool", m.Text, m.Ok));
                        break;
                    default:
                        Timeline.Add(new SystemItem(m.Text));
                        break;
                }
            }
            _history.AddRange(messages);
        }
        catch (OperationCanceledException)
        {
            Timeline.Add(new SystemItem("Cancelled."));
        }
        finally
        {
            IsThinking = false;
            Presence = "Ready";
        }
    }
}

/// <summary>Placeholder gate: refuse until the real preview dialog exists.</summary>
public sealed class UserApprovalGate : Core.Tools.IApprovalGate
{
    public Task<bool> RequestAsync(Core.Tools.ApprovalRequest request, CancellationToken ct = default)
        => Task.FromResult(false);
}
