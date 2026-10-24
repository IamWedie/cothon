namespace Cothon.Core.Sessions;

/// <summary>
/// Voice-first session phases render as the orb's motion and header status.
/// </summary>
public enum VoiceSessionState
{
    Idle,
    Listening,
    Thinking,
    Speaking,
}

/// <summary>
/// Mic is armed/disarmed by the hotkey or orb tap; a turn in flight locks
/// the toggle until it lands. Typed input still works with the mic off.
/// </summary>
public sealed class VoiceSession
{
    private bool _micOn;

    public VoiceSessionState State { get; private set; } = VoiceSessionState.Idle;

    public bool MicOn => _micOn;

    /// <summary>Raised only on real state changes; caller keeps it on the UI thread.</summary>
    public event Action? Changed;

    public void ToggleMic()
    {
        if (State is VoiceSessionState.Thinking or VoiceSessionState.Speaking)
            return;
        _micOn = !_micOn;
        Set(_micOn ? VoiceSessionState.Listening : VoiceSessionState.Idle);
    }

    public void StartThinking()
    {
        if (State is VoiceSessionState.Idle or VoiceSessionState.Listening)
            Set(VoiceSessionState.Thinking);
    }

    public void StartSpeaking()
    {
        if (State == VoiceSessionState.Thinking)
            Set(VoiceSessionState.Speaking);
    }

    public void EndTurn()
    {
        if (State is VoiceSessionState.Speaking)
            Set(_micOn ? VoiceSessionState.Listening : VoiceSessionState.Idle);
    }

    private void Set(VoiceSessionState state)
    {
        if (State == state)
            return;
        State = state;
        Changed?.Invoke();
    }
}
