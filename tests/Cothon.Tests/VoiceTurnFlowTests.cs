using Cothon.Core.Audio;
using Cothon.Core.Sessions;
using Cothon.Core.Speech;
using Cothon.Ui.ViewModels;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// The whole voice round trip: arm the mic, speak, silence closes the turn,
/// Gemini says what was said, and the assistant answers â€” mic stays armed.
/// </summary>
public sealed class VoiceTurnFlowTests
{
    private static AudioFrame Loud() => AudioFrame.Loud(1600, 9000);

    private static AudioFrame Quiet() => AudioFrame.Silence(160);

    private sealed class FakeMic : IAudioSource
    {
        public event Action<AudioFrame>? Frame;

        public void Start() { }

        public void Say(AudioFrame frame) => Frame?.Invoke(frame);

        public void Stop() { }
    }

    private static AgentLoop Loop(IModelProvider provider) => new(
        provider,
        new Core.Tools.ToolBroker(
            new Core.Tools.ToolRegistry(),
            new Core.Policy.PolicyEngine(),
            new Core.Tools.AutoApprovalGate(),
            new Core.Undo.UndoLedger()));
    // no gate needed: echo provider never calls tools, approval card path tested elsewhere

    private static MainWindowViewModel VoiceVm(FakeMic mic)
    {
        var vm = new MainWindowViewModel(Loop(new EchoModelProvider()))
        {
            UiPost = action => action(),
            MicSourceFactory = () => mic,
            Transcription = _ => Task.FromResult<string?>("open the window"),
        };
        return vm;
    }

    [Fact]
    public async Task Speak_then_silence_runs_a_full_turn_and_keeps_listening()
    {
        var mic = new FakeMic();
        var vm = VoiceVm(mic);
        vm.ToggleMic();

        Assert.True(vm.MicOn);
        Assert.Equal(VoiceSessionState.Listening, vm.SessionState);

        mic.Say(Loud());
        mic.Say(Loud());
        for (var i = 0; i < 120; i++)
            mic.Say(Quiet());
        mic.Say(Loud());
        mic.Say(Loud());
        for (var i = 0; i < 120; i++)
            mic.Say(Quiet());

        await WaitUntil(() => vm.SessionState == VoiceSessionState.Speaking);
        await WaitUntil(() => vm.SessionState == VoiceSessionState.Listening);

        Assert.Equal("open the window", vm.UserCaption);
        Assert.Contains(vm.Timeline, item => item is AssistantItem);
        Assert.True(vm.MicOn); // still armed for the next sentence
    }

    [Fact]
    public async Task Empty_transcript_asks_instead_of_confabulating()
    {
        var mic = new FakeMic();
        var vm = VoiceVm(mic);
        vm.Transcription = _ => Task.FromResult<string?>(null);
        vm.ToggleMic();

        mic.Say(Loud());
        mic.Say(Loud());
        for (var i = 0; i < 120; i++)
            mic.Say(Quiet());

        await WaitUntil(() => vm.Timeline.Any(item => item.Text.Contains("didn't say anything")));
        Assert.Equal(VoiceSessionState.Listening, vm.SessionState);
    }

    [Fact]
    public async Task Transcription_failure_is_quoted_not_swallowed()
    {
        var mic = new FakeMic();
        var vm = VoiceVm(mic);
        vm.Transcription = _ => throw new HttpRequestException("401 boom");
        vm.ToggleMic();

        mic.Say(Loud());
        mic.Say(Loud());
        for (var i = 0; i < 120; i++)
            mic.Say(Quiet());

        await WaitUntil(() => vm.Timeline.Any(item => item.Text.Contains("401 boom")));
    }

    [Fact]
    public void Toggle_off_leaves_a_clean_state_and_no_pending_recording()
    {
        var mic = new FakeMic();
        var vm = VoiceVm(mic);
        vm.ToggleMic();
        mic.Say(Loud());
        vm.ToggleMic();

        Assert.False(vm.MicOn);
        Assert.Equal(VoiceSessionState.Idle, vm.SessionState);
        Assert.Equal(0.0, vm.MicLevel);
    }

    [Fact]
    public void Level_follows_the_mic_while_listening()
    {
        var mic = new FakeMic();
        var vm = VoiceVm(mic);
        vm.ToggleMic();

        mic.Say(Loud());

        Assert.True(vm.MicLevel > 0.2);
    }

    [Fact]
    public async Task Final_answer_is_spoken_through_the_player_seam()
    {
        var mic = new FakeMic();
        var vm = VoiceVm(mic);
        var spoken = new List<string>();
        vm.Speaker = text =>
        {
            spoken.Add(text);
            return Task.FromResult<SpokenAudio?>(new SpokenAudio(PcmBytes: [1, 2], SampleRate: 24000, MimeType: "audio/L16"));
        };
        var played = new List<SpokenAudio>();
        vm.Player = new PlaySink(played);
        vm.ToggleMic();

        mic.Say(Loud());
        mic.Say(Loud());
        for (var i = 0; i < 120; i++)
            mic.Say(Quiet());

        await WaitUntil(() => played.Count > 0);
        Assert.Equal("[echo] open the window", spoken.Single());
    }

    private sealed class PlaySink(List<SpokenAudio> sink) : IAudioPlayer
    {
        public void Play(SpokenAudio audio) => sink.Add(audio);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }
        Assert.True(condition(), "condition not met in time");
    }
}

