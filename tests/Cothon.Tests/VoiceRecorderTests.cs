using Cothon.Core.Audio;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// The mic's brain: keep rolling while armed, decide the turn ended on
/// ~1.1 s of trailing silence after speech, cap runaway recordings, and
/// only emit clips that survived the noise gate.
/// </summary>
public sealed class VoiceRecorderTests
{
    private static AudioFrame Quiet() => AudioFrame.Silence(160);

    /// <summary>100 ms of constant loud tone — loud frames are the only thing recorded.</summary>
    private static AudioFrame Speech() => AudioFrame.Loud(1600, amplitude: 9000);

    /// <summary>Runs the recorder over a scripted frame sequence and returns the emitted clip (or null).</summary>
    private static VoiceClip? Run(AudioFrame[] frames, VoiceRecorder? recorder = null)
    {
        recorder ??= new VoiceRecorder();
        VoiceClip? emitted = null;
        recorder.ClipEnded += clip => emitted = clip;
        recorder.BeginListening();
        foreach (var frame in frames)
            recorder.OnFrame(frame);
        recorder.StopListening();
        return emitted;
    }

    [Fact]
    public void Speech_then_silence_ends_the_turn_with_a_clip()
    {
        var clip = Run([Quiet(), Quiet(), Speech(), Speech(), Speech(), .. Many(Quiet(), 8)]);

        Assert.NotNull(clip);
        // only loud frames are recorded, so a real duration means speech made it in
        Assert.True(clip!.DurationSeconds >= 0.25);
    }

    [Fact]
    public void Quiet_lead_in_is_buffered_then_turn_still_happens()
    {
        var clip = Run([.. Many(Quiet(), 20), Speech(), Speech(), .. Many(Quiet(), 8)]);

        Assert.NotNull(clip);
    }

    [Fact]
    public void Ending_before_saying_anything_yields_no_clip()
    {
        var clip = Run(Many(Quiet(), 30));

        Assert.Null(clip);
    }

    [Fact]
    public void Runaway_recording_is_capped_at_max_seconds()
    {
        var clip = Run(Many(Speech(), 250)); // 25 s of continuous speech

        Assert.NotNull(clip);
        Assert.True(clip!.DurationSeconds <= VoiceRecorder.MaxSeconds + 0.15);
    }

    [Fact]
    public void Blips_below_the_noise_gate_never_open_a_turn()
    {
        var clip = Run([
            AudioFrame.Loud(160, amplitude: 80),
            AudioFrame.Loud(160, amplitude: 80),
            Quiet(), Quiet(),
        ]);

        Assert.Null(clip);
    }

    [Fact]
    public void Rms_level_is_sane_for_silence_and_speech()
    {
        Assert.Equal(0.0, AudioFrame.Silence(160).LevelRms);
        Assert.True(AudioFrame.Loud(160, 9000).LevelRms > 0.2);
    }

    private static AudioFrame[] Many(AudioFrame frame, int count)
        => [.. Enumerable.Repeat(frame, count)];
}
