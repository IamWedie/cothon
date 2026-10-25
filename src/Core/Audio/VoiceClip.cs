namespace Cothon.Core.Audio;

/// <summary>
/// A captured voice clip: 16 kHz mono 16-bit PCM wrapped in a standard WAV
/// container, ready for an inline-data transcription request.
/// </summary>
public sealed record VoiceClip(byte[] PcmBytes, int SampleRate = 16000)
{
    /// <summary>Anything shorter is breath/noise; never send it on.</summary>
    public const double MinimumSeconds = 0.15;

    public double DurationSeconds => PcmBytes.Length / (double)(SampleRate * 2);

    public string MimeType => "audio/wav";

    public static VoiceClip? FromPcm(byte[] pcm, int sampleRate = 16000, double minimumSeconds = MinimumSeconds)
    {
        if (pcm.Length < 2)
            return null;
        if (pcm.Length / (double)(sampleRate * 2) < minimumSeconds)
            return null;
        return new VoiceClip(pcm, sampleRate);
    }

    /// <summary>44-byte canonical RIFF/WAVE header + PCM payload.</summary>
    public byte[] WavBytes
    {
        get
        {
            var pcm = PcmBytes.Length;
            var wav = new byte[44 + pcm];

            WriteAscii(wav, 0, "RIFF");
            BitConverter.GetBytes(36 + pcm).CopyTo(wav, 4);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            BitConverter.GetBytes(16).CopyTo(wav, 16);          // fmt chunk size
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);    // PCM
            BitConverter.GetBytes((short)1).CopyTo(wav, 22);    // mono
            BitConverter.GetBytes(SampleRate).CopyTo(wav, 24);
            BitConverter.GetBytes(SampleRate * 2).CopyTo(wav, 28); // byte rate
            BitConverter.GetBytes((short)2).CopyTo(wav, 32);    // block align
            BitConverter.GetBytes((short)16).CopyTo(wav, 34);   // bits per sample
            WriteAscii(wav, 36, "data");
            BitConverter.GetBytes(pcm).CopyTo(wav, 40);
        // IReadOnlyList semantics kept for consumers; array backing enables CopyTo.
        PcmBytes.CopyTo(wav, 44);
        return wav;
        }
    }

    private static void WriteAscii(byte[] target, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++)
            target[offset + i] = (byte)text[i];
    }
}
