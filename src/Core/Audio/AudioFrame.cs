namespace Cothon.Core.Audio;

/// <summary>One mic chunk, 16 kHz mono 16-bit PCM, with an instant level read.</summary>
public sealed record AudioFrame(short[] Samples)
{
    public double LevelRms
    {
        get
        {
            if (Samples.Length == 0)
                return 0.0;
            double sum = 0;
            foreach (var s in Samples)
                sum += (double)s * s;
            return Math.Sqrt(sum / Samples.Length) / short.MaxValue;
        }
    }

    public static AudioFrame Silence(int sampleCount) => new(new short[sampleCount]);

    public static AudioFrame Loud(int sampleCount, short amplitude) =>
        new(Enumerable.Repeat(amplitude, sampleCount).ToArray());
}

/// <summary>The physical mic: start pumping frames, stop and release the device.</summary>
public interface IAudioSource
{
    void Start();

    event Action<AudioFrame>? Frame;

    void Stop();
}
