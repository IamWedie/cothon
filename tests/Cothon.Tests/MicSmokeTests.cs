using Cothon.Core.Audio;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// Live-machine mic smoke: with a capture device present the NAudio
/// pipeline must pump real 16 kHz frames. Skips silently on machines
/// with no microphone so CI stays green.
/// </summary>
public sealed class MicSmokeTests
{
    [Fact]
    public async Task Windows_mic_pumps_16k_frames_and_levels_move()
    {
        if (!HasCaptureDevice())
            return;

        using var mic = new WindowsMicSource();
        var frames = 0;
        var peak = 0.0;
        var errors = new List<string>();
        mic.Frame += frame =>
        {
            Interlocked.Increment(ref frames);
            var level = frame.LevelRms;
            if (level > peak)
                peak = level;
        };
        mic.Error += errors.Add;

        mic.Start();
        await Task.Delay(1200);
        mic.Stop();

        Assert.True(frames >= 40,
            $"expected a streaming mic, got {frames} frames in 1.2 s (errors: {string.Join("; ", errors)})");
    }

    private static bool HasCaptureDevice()
    {
        try
        {
            using var device = new NAudio.CoreAudioApi.MMDeviceEnumerator()
                .GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console);
            return device is not null;
        }
        catch
        {
            return false;
        }
    }
}
