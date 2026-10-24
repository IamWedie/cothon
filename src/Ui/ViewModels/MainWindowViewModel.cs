using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cothon.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly Core.Sessions.AgentLoop _loop;
    private readonly List<Core.Sessions.ChatMessage> _history = [];

    [ObservableProperty]
    private string _presence = "Idle — listening is off";

    [ObservableProperty]
    private string _input = "";

    public ObservableCollection<TimelineItem> Timeline { get; } = [];

    public MainWindowViewModel()
        : this(BuildDefaultLoop())
    {
    }

    public MainWindowViewModel(Core.Sessions.AgentLoop loop)
    {
        _loop = loop;
        Timeline.Add(new TimelineItem("system", "Ready. Tools are gated by policy.", "system"));
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
        if (text.Length == 0) return;
        Input = "";

        Presence = "Thinking…";
        try
        {
            var messages = await _loop.RunAsync(_history, text, ct);
            foreach (var m in messages)
            {
                var label = m.Author switch
                {
                    Core.Sessions.Author.User => "you",
                    Core.Sessions.Author.Assistant => "cothon",
                    Core.Sessions.Author.Tool => $"tool: {m.ToolName}",
                    _ => "system",
                };
                Timeline.Add(new TimelineItem(label, m.Text, DefaultKind(m)));
            }
            _history.AddRange(messages);
        }
        catch (OperationCanceledException)
        {
            Timeline.Add(new TimelineItem("system", "Cancelled.", "system"));
        }
        finally
        {
            Presence = "Idle — listening is off";
        }
    }

    private static string DefaultKind(Core.Sessions.ChatMessage m) =>
        m.Author == Core.Sessions.Author.Tool && !m.Ok ? "error" : m.Author.ToString().ToLowerInvariant();
}

public sealed record TimelineItem(string Label, string Text, string Kind);

/// <summary>Placeholder gate: refuse until the real preview dialog exists.</summary>
public sealed class UserApprovalGate : Core.Tools.IApprovalGate
{
    public Task<bool> RequestAsync(Core.Tools.ApprovalRequest request, CancellationToken ct = default)
        => Task.FromResult(false);
}
