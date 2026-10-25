using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cothon.Core.Audio;

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
    private readonly VoiceRecorder _recorder = new();
    private readonly List<Core.Sessions.ChatMessage> _history = [];
    private readonly HashSet<string> _alwaysApproved = [];
    private TaskCompletionSource<bool>? _approvalTcs;
    private IAudioSource? _mic;
    private bool _capturing;

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

    [ObservableProperty]
    private bool _drawerOpen;

    [ObservableProperty]
    private ApprovalCard? _pendingApproval;

    /// <summary>Live mic loudness 0..1, drives the orb while listening.</summary>
    [ObservableProperty]
    private double _micLevel;

    public ObservableCollection<TimelineItem> Timeline { get; } = [];

    public bool HasUserCaption => UserCaption.Length > 0;

    public bool HasPendingApproval => PendingApproval is not null;

    /// <summary>Mic seam for tests; default is the real Windows capture.</summary>
    internal Func<IAudioSource>? MicSourceFactory { get; set; } = () => new WindowsMicSource();

    /// <summary>Transcription seam for tests; default uses Gemini with the stored key.</summary>
    internal Func<VoiceClip, Task<string?>>? Transcription { get; set; }

    /// <summary>Realtime handoff to the UI thread; tests run it inline.</summary>
    internal Action<Action> UiPost { get; set; } = action => Dispatcher.UIThread.Post(action);

    public MainWindowViewModel()
    {
        var gate = new ForwardGate();
        _loop = BuildDefaultLoop(gate);
        gate.Bind(this);
        _session.Changed += () =>
        {
            SessionState = _session.State;
            MicOn = _session.MicOn;
            Presence = StateText(_session.State);
        };
        _recorder.ClipEnded += clip => UiPost(() => OnVoiceClip(clip));

        // A provider chosen in Settings without a key is spoken, not silent.
        var stored = Core.Settings.AppSettings.Load();
        if (needsKey(stored.ModelProvider) && !hasKey(stored, stored.ModelProvider))
            NoteSystem($"{stored.ModelProvider} is selected but its API key is empty — using echo until you paste one in Settings.");
    }

    private static bool needsKey(string provider) =>
        provider is "gemini" or "openai" or "claude";

    private static bool hasKey(Core.Settings.AppSettings stored, string provider) => provider switch
    {
        "gemini" => !string.IsNullOrWhiteSpace(stored.GeminiApiKey),
        "openai" => !string.IsNullOrWhiteSpace(stored.OpenAiApiKey),
        "claude" => !string.IsNullOrWhiteSpace(stored.AnthropicApiKey),
        _ => false,
    };

    internal MainWindowViewModel(Core.Sessions.AgentLoop loop)
    {
        _loop = loop;
        _session.Changed += () =>
        {
            SessionState = _session.State;
            MicOn = _session.MicOn;
            Presence = StateText(_session.State);
        };
        _recorder.ClipEnded += clip => UiPost(() => OnVoiceClip(clip));
    }

    private static string StateText(Core.Sessions.VoiceSessionState state) => state switch
    {
        Core.Sessions.VoiceSessionState.Listening => "Mic armed",
        Core.Sessions.VoiceSessionState.Thinking => "Thinking…",
        Core.Sessions.VoiceSessionState.Speaking => "Speaking",
        _ => "Ready",
    };

    private static Core.Sessions.AgentLoop BuildDefaultLoop(ForwardGate gate)
    {
        var registry = new Core.Tools.ToolRegistry();
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cothon", "sandbox");
        registry.Register(new Core.Files.FileTool(new Core.Files.PathPolicy(root)));

        var stored = Core.Settings.AppSettings.Load();
        ScreenCaptureEnabled = stored.ScreenCapture;
        Core.Memory.MemoryStore? memory = null;
        if (stored.MemoryLogging)
        {
            memory = new Core.Memory.MemoryStore(stored.MemoryLogging);
            registry.Register(new Core.Memory.MemoryTool(memory));
        }
        var adb = Core.Phone.AdbProbe.ResolveAdb(
            stored.AdbPath,
            Environment.GetEnvironmentVariable("PATH") ?? "",
            Core.Phone.AdbProbe.DefaultCommonDirs);
        if (adb.Length > 0)
            registry.Register(new Core.Phone.PhoneTool(
                new Core.Phone.AdbClient(adb, stored.PhoneHost, stored.PhonePort),
                new Core.Files.PathPolicy(root)));
        var provider = ChooseProvider(stored);
        return new Core.Sessions.AgentLoop(
            provider,
            new Core.Tools.ToolBroker(
                registry, new Core.Policy.PolicyEngine(), gate, new Core.Undo.UndoLedger()),
            memory);
    }

    /// <summary>Gemini only with a key present; echo stays the quiet fallback.</summary>
    internal static Core.Sessions.IModelProvider ChooseProvider(Core.Settings.AppSettings stored)
    {
        var key = stored.ModelProvider switch
        {
            "gemini" => stored.GeminiApiKey,
            "openai" => stored.OpenAiApiKey,
            "claude" => stored.AnthropicApiKey,
            _ => "",
        };
        return KeyedProvider(stored.ModelProvider, key);
    }

    private static Core.Sessions.IModelProvider KeyedProvider(string name, string key)
        => (name, string.IsNullOrWhiteSpace(key)) switch
        {
            ("gemini", false) => new Core.Providers.GeminiTextProvider(key),
            ("openai", false) => new Core.Providers.OpenAiTextProvider(key),
            ("claude", false) => new Core.Providers.AnthropicTextProvider(key),
            _ => new Core.Sessions.EchoModelProvider(),
        };

    /// <summary>Screen-reading switch honoured per user choice; seam for tests.</summary>
    internal static bool ScreenCaptureEnabled { get; private set; }

    internal Func<Core.Screen.ScreenFrame?> ScreenGetter { get; set; } =
        Core.Screen.SystemScreenCapture.Capture;

    /// <summary>Hotkey and orb tap both land here; Core guards mid-turn calls.</summary>
    [RelayCommand]
    public void ToggleMic() => _session.ToggleMic();

    partial void OnMicOnChanged(bool value)
    {
        if (value)
            StartVoiceCapture();
        else
            StopVoiceCapture();
    }

    private void StartVoiceCapture()
    {
        if (_capturing)
            return;
        _capturing = true;
        _recorder.BeginListening();
        var mic = MicSourceFactory?.Invoke();
        if (mic is null)
        {
            NoteSystem("No microphone source is available this session.");
            _capturing = false;
            return;
        }
        _mic = mic;
        mic.Start();
        if (mic is WindowsMicSource windows)
            windows.Error += message => UiPost(() =>
            {
                NoteSystem(message);
                if (_session.State is Core.Sessions.VoiceSessionState.Listening)
                    StopVoiceCapture();
            });
        mic.Frame += frame => UiPost(() =>
        {
            _recorder.OnFrame(frame);
            MicLevel = _recorder.Level;
        });
        if (mic is not WindowsMicSource)
            NoteSystem("Only a stub microphone is active — real audio needs the Windows host.");
    }

    private void StopVoiceCapture()
    {
        if (!_capturing)
            return;
        _capturing = false;
        MicLevel = 0;
        // let a mid-speech clip flush before the device goes away
        _recorder.StopListening();
        _mic?.Stop();
        if (_mic is IDisposable disposable)
            disposable.Dispose();
        _mic = null;
    }

    private void OnVoiceClip(Core.Audio.VoiceClip clip)
    {
        if (!_capturing)
            return;
        _ = SpeakTurnAsync(clip);
    }

    private async Task SpeakTurnAsync(VoiceClip clip)
    {
        string? transcript = null;
        try
        {
            transcript = Transcription is not null
                ? await Transcription(clip)
                : await TranscribeForRealAsync(clip);
        }
        catch (Exception ex)
        {
            NoteSystem($"I couldn't transcribe that: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            NoteSystem("You didn't say anything I could make out.");
        }
        else
        {
            await RunTurn(transcript, CancellationToken.None);
        }

        _recorder.BeginListening(); // keep the conversation rolling while armed
    }

    /// <summary>Speak turn seam for tests; default synthesizes through Gemini TTS.</summary>
    internal Func<string, Task<Cothon.Core.Speech.SpokenAudio?>>? Speaker { get; set; }

    /// <summary>Playback sink for tests; default is the Windows wave player.</summary>
    internal Core.Speech.IAudioPlayer? Player { get; set; } = new Core.Speech.WindowsWavePlayer();

    private async Task SpeakOut(string text)
    {
        // in test-shaped VMs (Transcription seam set) stay silent unless a speaker seam is explicit
        if (Speaker is null && Transcription is not null)
            return;

        Func<string, Task<Cothon.Core.Speech.SpokenAudio?>>? speaker =
            Speaker ?? await RealSpeakerAsync();
        if (speaker is null)
            return;
        try
        {
            var audio = await speaker(text).ConfigureAwait(false);
            if (audio is not null)
                Player?.Play(audio);
        }
        catch (Exception ex)
        {
            UiPost(() => NoteSystem($"Speaking could not run: {ex.Message}"));
        }
    }

    /// <summary>Real speech needs Gemini voice support and a key; other providers are text-only.</summary>
    internal Task<Func<string, Task<Cothon.Core.Speech.SpokenAudio?>>?> RealSpeakerAsync()
    {
        var settings = Core.Settings.AppSettings.Load();
        if (!settings.SpeechEnabled || settings.ModelProvider is not "gemini" || string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            return Task.FromResult<Func<string, Task<Cothon.Core.Speech.SpokenAudio?>>?>(null);

        var speaker = new Core.Speech.GeminiSpeaker(settings.GeminiApiKey);
        Func<string, Task<Cothon.Core.Speech.SpokenAudio?>> call = text => speaker.SpeakAsync(text);
        return Task.FromResult<Func<string, Task<Cothon.Core.Speech.SpokenAudio?>>?>(call);
    }

    internal async Task<string?> TranscribeForRealAsync(VoiceClip clip)    {
        var settings = Core.Settings.AppSettings.Load();
        if (settings.ModelProvider is not "gemini" || string.IsNullOrWhiteSpace(settings.GeminiApiKey))
        {
            NoteSystem("Speech understanding needs the Gemini provider and a key — speaking only works there right now.");
            return null;
        }
        return await new GeminiTranscriber(settings.GeminiApiKey).TranscribeAsync(clip);
    }

    /// <summary>Visible system notes (hotkey failures etc.) land in captions + timeline.</summary>
    public void NoteSystem(string text)
    {
        Timeline.Add(new SystemItem(text));
        AssistantCaption = text;
    }

    /// <summary>Broker asks the human: show the card, wait for the answer.</summary>
    public Task<bool> RequestApprovalAsync(Core.Tools.ApprovalRequest request, CancellationToken ct = default)
    {
        if (_alwaysApproved.Contains(request.ToolName))
            return Task.FromResult(true);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approvalTcs = tcs;
        PendingApproval = new ApprovalCard(
            request.ToolName,
            $"Allow {request.ToolName}?",
            Describe(request));
        OnPropertyChanged(nameof(HasPendingApproval));
        return tcs.Task;
    }

    private static string Describe(Core.Tools.ApprovalRequest request)
    {
        var args = string.Join("  ·  ",
            request.Args.Select(kv => $"{kv.Key} = {kv.Value}"));
        var suffix = args.Length > 0 ? $"\n{args}" : "";
        return request.Definition.Description + suffix;
    }

    private void ResolveApproval(bool allow, bool remember)
    {
        if (_approvalTcs is null)
            return;
        if (remember && PendingApproval is not null)
            _alwaysApproved.Add(PendingApproval.ToolName);
        var tcs = _approvalTcs;
        _approvalTcs = null;
        PendingApproval = null;
        OnPropertyChanged(nameof(HasPendingApproval));
        tcs.TrySetResult(allow);
    }

    [RelayCommand]
    public void ApproveOnce() => ResolveApproval(true, remember: false);

    [RelayCommand]
    public void ApproveAlways() => ResolveApproval(true, remember: true);

    [RelayCommand]
    public void DenyApproval() => ResolveApproval(false, remember: false);

    [RelayCommand]
    public void ToggleDrawer() => DrawerOpen = !DrawerOpen;

    [RelayCommand]
    private async Task SendAsync(CancellationToken ct)
    {
        var text = Input.Trim();
        if (text.Length == 0 || _session.State is Core.Sessions.VoiceSessionState.Thinking
            or Core.Sessions.VoiceSessionState.Speaking)
            return;
        Input = "";
        await RunTurn(text, ct);
    }

    private async Task RunTurn(string text, CancellationToken ct)
    {
        if (_session.State is Core.Sessions.VoiceSessionState.Thinking
            or Core.Sessions.VoiceSessionState.Speaking)
            return;
        HasMessages = true;
        UserCaption = text;
        AssistantCaption = "";
        _session.StartThinking();
        Core.Screen.ScreenFrame? frame = ScreenCaptureEnabled ? ScreenGetter() : null;
        if (frame is not null)
            Timeline.Add(new SystemItem($"Screen captured ({frame.Width}×{frame.Height}) and read with this message."));
        try
        {
            var messages = await _loop.RunAsync(_history, text, frame, ct);
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
        _ = SpeakOut(said?.Text ?? "Done.");
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

/// <summary>Content shown on the approval card above the input.</summary>
public sealed record ApprovalCard(string ToolName, string Title, string Detail);

/// <summary>
/// Broker gate seam: forwards approval asks into the VM's pending card;
/// denies until bound and remembers "always allow" per tool for the session.
/// </summary>
public sealed class ForwardGate : Core.Tools.IApprovalGate
{
    private MainWindowViewModel? _vm;

    public void Bind(MainWindowViewModel vm) => _vm = vm;

    public Task<bool> RequestAsync(Core.Tools.ApprovalRequest request, CancellationToken ct = default)
        => _vm?.RequestApprovalAsync(request, ct) ?? Task.FromResult(false);
}
