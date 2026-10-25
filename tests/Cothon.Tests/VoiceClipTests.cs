using Cothon.Core.Audio;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// Voice clips: 16 kHz mono 16-bit PCM wrapped in a WAV container that any
/// transcription endpoint accepts without extra flags.
/// </summary>
public sealed class VoiceClipTests
{
    [Fact]
    public void Empty_pcm_makes_no_clip()
    {
        Assert.Null(VoiceClip.FromPcm([]));
        Assert.Null(VoiceClip.FromPcm([1, 2])); // less than one sample
    }

    [Fact]
    public void Clip_reports_duration_and_speaks_its_rate()
    {
        // 60 ms of 16 kHz mono 16-bit PCM = 960 samples = 1920 bytes.
        var clip = VoiceClip.FromPcm(new byte[1920], minimumSeconds: 0);

        Assert.NotNull(clip);
        Assert.Equal(0.06, clip!.DurationSeconds, precision: 3);
        Assert.Equal(16000, clip.SampleRate);
        Assert.Equal("audio/wav", clip.MimeType);
    }

    [Fact]
    public void Riff_header_is_correct_and_wraps_the_pcm()
    {
        var pcm = new byte[10];
        var clip = VoiceClip.FromPcm(pcm, minimumSeconds: 0)!;

        var wav = clip.WavBytes;

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal(16, BitConverter.ToInt32(wav, 16));          // fmt chunk size
        Assert.Equal((short)1, BitConverter.ToInt16(wav, 20));    // PCM
        Assert.Equal((short)1, BitConverter.ToInt16(wav, 22));    // mono
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));       // sample rate
        Assert.Equal(32000, BitConverter.ToInt32(wav, 28));       // byte rate
        Assert.Equal((short)2, BitConverter.ToInt16(wav, 32));    // block align
        Assert.Equal((short)16, BitConverter.ToInt16(wav, 34));   // bits
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(10, BitConverter.ToInt32(wav, 40));          // data size
        Assert.Equal(44 + 10, wav.Length);                        // header + pcm
    }

    [Fact]
    public void Short_blips_are_rejected_as_noise()
    {
        // less than a 150 ms minimum speech window
        var clip = VoiceClip.FromPcm(new byte[3200]); // 100 ms
        Assert.Null(clip);
    }
}
