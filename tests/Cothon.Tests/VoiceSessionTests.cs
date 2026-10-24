using Cothon.Core.Sessions;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// Voice-first session state machine: mic toggle drives Listening/Idle,
/// turns flow Thinking → Speaking → back, hotkey is inert mid-turn.
/// </summary>
public sealed class VoiceSessionTests
{
    [Fact]
    public void Starts_idle_with_mic_off()
    {
        var s = new VoiceSession();

        Assert.Equal(VoiceSessionState.Idle, s.State);
        Assert.False(s.MicOn);
    }

    [Fact]
    public void Toggle_mic_enters_listening_and_back()
    {
        var s = new VoiceSession();

        s.ToggleMic();
        Assert.True(s.MicOn);
        Assert.Equal(VoiceSessionState.Listening, s.State);

        s.ToggleMic();
        Assert.False(s.MicOn);
        Assert.Equal(VoiceSessionState.Idle, s.State);
    }

    [Fact]
    public void Turn_with_mic_on_returns_to_listening()
    {
        var s = new VoiceSession();
        s.ToggleMic(); // Listening

        s.StartThinking();
        Assert.Equal(VoiceSessionState.Thinking, s.State);
        Assert.True(s.MicOn);

        s.StartSpeaking();
        Assert.Equal(VoiceSessionState.Speaking, s.State);

        s.EndTurn();
        Assert.Equal(VoiceSessionState.Listening, s.State);
        Assert.True(s.MicOn);
    }

    [Fact]
    public void Typed_turn_without_mic_returns_to_idle()
    {
        var s = new VoiceSession(); // Idle, mic off

        s.StartThinking(); // user typed instead of talking
        Assert.Equal(VoiceSessionState.Thinking, s.State);
        Assert.False(s.MicOn);

        s.StartSpeaking();
        s.EndTurn();
        Assert.Equal(VoiceSessionState.Idle, s.State);
    }

    [Fact]
    public void Mic_toggle_is_inert_mid_turn()
    {
        var s = new VoiceSession();
        s.ToggleMic();
        s.StartThinking();

        s.ToggleMic();
        Assert.Equal(VoiceSessionState.Thinking, s.State);
        Assert.True(s.MicOn);

        s.StartSpeaking();
        s.ToggleMic();
        Assert.Equal(VoiceSessionState.Speaking, s.State);
        Assert.True(s.MicOn);
    }

    [Fact]
    public void Invalid_transitions_are_no_ops()
    {
        var s = new VoiceSession();

        s.StartSpeaking(); // cannot speak before thinking
        Assert.Equal(VoiceSessionState.Idle, s.State);
    }

    [Fact]
    public void Changed_fires_only_on_real_transitions()
    {
        var s = new VoiceSession();
        var changes = 0;
        s.Changed += () => changes++;

        s.ToggleMic();      // Idle -> Listening
        s.StartThinking();  // Listening -> Thinking
        s.StartThinking();  // already Thinking
        s.StartSpeaking();  // -> Speaking
        s.EndTurn();        // -> Listening
        s.EndTurn();        // from Listening: no-op

        Assert.Equal(4, changes);
    }
}
