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
    private readonly Core.Sessions.VoiceSession _session = new();
    private readonly List<Core.Sessions.ChatMessage> _history = [];

    [ObservableProperty]
    private string _presence = StateText(Core.Sessions.VoiceSessionState.Idle);

    [ObservableProperty]
    private Core.Sessions.VoiceSessionState _sessionState = Core.Sessions.VoiceSessionState.Idle;

    [ObservableProperty]
    private bool _micOn;

    [ObservableProperty]
    private string _userCaption = "";

    [ObservableProperty]
    private string _assistantCaption = "How can I help?";

    [ObservableProperty]
    private string _input = "";

    [ObservableProperty]
    private bool _hasMessages;

    public ObservableCollection<TimelineItem> Timeline { get; } = [];

    public bool HasUserCaption => UserCaption.Length > 0;

    public MainWindowViewModel()
        : this(BuildDefaultLoop())
    {
    }

    public MainWindowViewModel(Core.Sessions.AgentLoop loop)
    {
        _loop = loop;
        _session.Changed += () =>
        {
            SessionState = _session.State;
            MicOn = _session.MicOn;
            Presence = StateText(_session.State);
        };
    }

    private static string StateText(Core.Sessions.VoiceSessionState state) => state switch
    {
        Core.Sessions.VoiceSessionState.Listening => "Mic armed",
        Core.Sessions.VoiceSessionState.Thinking => "Thinking…",
        Core.Sessions.VoiceSessionState.Speaking => "Speaking",
        _ => "Ready",
    };

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

    /// <summary>Hotkey and orb tap both land here; Core guards mid-turn calls.</summary>
    [RelayCommand]
    public void ToggleMic() => _session.ToggleMic();

    /// <summary>Visible system notes (hotkey failures etc.) land in captions + timeline.</summary>
    public void NoteSystem(string text)
    {
        Timeline.Add(new SystemItem(text));
        AssistantCaption = text;
    }

    [RelayCommand]
    private async Task SendAsync(CancellationToken ct)
    {
        var text = Input.Trim();
        if (text.Length == 0 || _session.State is Core.Sessions.VoiceSessionState.Thinking
            or Core.Sessions.VoiceSessionState.Speaking)
            return;
        Input = "";
        HasMessages = true;
        UserCaption = text;
        AssistantCaption = "";
        _session.StartThinking();
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

            var said = messages.LastOrDefault(m => m.Author == Core.Sessions.Author.Assistant);
            AssistantCaption = said is { Text.Length: > 0 } ? said.Text : "Done.";
            _session.StartSpeaking();
            await Task.Delay(700, ct);
            _session.EndTurn();
        }
        catch (OperationCanceledException)
        {
            AssistantCaption = "Cancelled.";
            _session.EndTurn();
        }
    }

    partial void OnUserCaptionChanged(string value) => OnPropertyChanged(nameof(HasUserCaption));
}

/// <summary>Placeholder gate: refuse until the real preview dialog exists.</summary>
public sealed class UserApprovalGate : Core.Tools.IApprovalGate
{
    public Task<bool> RequestAsync(Core.Tools.ApprovalRequest request, CancellationToken ct = default)
        => Task.FromResult(false);
}
