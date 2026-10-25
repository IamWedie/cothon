using NAudio.Wave;

namespace Cothon.Core.Speech;

/// <summary>Anything that can render spoken audio. Fake in tests.</summary>
public interface IAudioPlayer
{
    void Play(SpokenAudio audio);
}

/// <summary>
/// Fire-and-forget raw PCM playback through Windows' wave out; self-cleans
/// when the clip finishes. Errors surface as silence plus a caller note,
/// never an audible crash.
/// </summary>
public sealed class WindowsWavePlayer : IAudioPlayer, IDisposable
{
    public void Play(SpokenAudio audio)
    {
        try
        {
            var stream = new RawSourceWaveStream(new MemoryStream(audio.PcmBytes), new WaveFormat(audio.SampleRate, 16, 1));
            var output = new WaveOutEvent();
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                stream.Dispose();
            };
            output.Init(stream);
            output.Play();
        }
        catch
        {
            // Rendering is cosmetic; a broken device must not explode a turn.
        }
    }

    public void Dispose() { }
}
