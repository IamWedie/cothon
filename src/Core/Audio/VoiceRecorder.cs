namespace Cothon.Core.Audio;

/// <summary>
/// Turns a rolling mic stream into finished voice turns: speech opens the
/// window, ~1.1 s of trailing silence (or a 20 s runaway cap) closes it,
/// and pre-speech quiet is buffered out of the clip.
/// </summary>
public sealed class VoiceRecorder
{
    /// <summary>Relative loudness that counts as speech (0..1 RMS).</summary>
    public const double SpeechFloor = 0.05;

    public const double QuietFloor = 0.02;
    public const int SilenceCloseMs = 1100;
    public const double MaxSeconds = 20.0;
    private const int SampleRate = 16000;
    private const int LeadKeepBytes = 16000; // 0.5 s of lead-in protection

    private readonly MemoryStream _buffer = new();
    private bool _listening;
    private bool _speechSeen;
    private long _totalMs;
    private long _quietMs;

    /// <summary>Fires once the turn recording closes with enough voice.</summary>
    public event Action<VoiceClip>? ClipEnded;

    /// <summary>Instant loudness 0..1 of the last frame; drives orb visuals.</summary>
    public double Level { get; private set; }

    public bool IsRecording => _listening;

    public void BeginListening()
    {
        _buffer.SetLength(0);
        _listening = true;
        _speechSeen = false;
        Level = 0;
        _totalMs = 0;
        _quietMs = 0;
    }

    public void StopListening()
    {
        if (_listening && _speechSeen && VoiceIsLongEnough())
            Emit();
        EndOfTurn();
    }

    public void OnFrame(AudioFrame frame)
    {
        if (!_listening)
            return;

        var bytes = PcmOf(frame);
        var frameMs = bytes.Length * 1000L / (SampleRate * 2);
        Level = frame.LevelRms;
        _totalMs += frameMs;

        if (_speechSeen)
        {
            _buffer.Write(bytes, 0, bytes.Length);
            _quietMs = frame.LevelRms > QuietFloor ? 0 : _quietMs + frameMs;

            var capped = _totalMs >= MaxSeconds * 1000;
            var silent = _quietMs >= SilenceCloseMs;
            if (!capped && !silent)
                return;

            if (VoiceIsLongEnough())
                Emit();
            EndOfTurn();
            return;
        }

        if (frame.LevelRms >= SpeechFloor)
        {
            _speechSeen = true;
            _buffer.Write(bytes, 0, bytes.Length);
        }

        // no-speech frames are dropped so idle chatter never fills the clip
    }

    private bool VoiceIsLongEnough()
        => _buffer.Length / (double)(SampleRate * 2) >= VoiceClip.MinimumSeconds;

    private void Emit()
    {
        var clip = VoiceClip.FromPcm(_buffer.ToArray());
        if (clip is not null)
            ClipEnded?.Invoke(clip);
    }

    private void EndOfTurn()
    {
        _listening = false;
        _speechSeen = false;
        _buffer.SetLength(0);
        _quietMs = 0;
        _totalMs = 0;
        Level = 0;
    }

    private static byte[] PcmOf(AudioFrame frame)
    {
        var bytes = new byte[frame.Samples.Length * 2];
        for (var i = 0; i < frame.Samples.Length; i++)
        {
            var s = frame.Samples[i];
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }
}
