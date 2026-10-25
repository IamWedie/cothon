using NAudio.Wave;

namespace Cothon.Core.Audio;

/// <summary>
/// The real microphone: Windows' default capture device first, every other
/// capture device as fallback (the default may be a dead Bluetooth earbud),
/// 16 kHz mono 16-bit PCM out, 160-sample (10 ms) frames. Start/Stop may be
/// called from any thread; frames arrive on the capture thread.
/// </summary>
public sealed class WindowsMicSource : IAudioSource, IDisposable
{
    private const int TargetRate = 16000;
    private const int FrameSamples = 160;

    public event Action<AudioFrame>? Frame;

    /// <summary>Raised with a short human message when no mic can open at all.</summary>
    public event Action<string>? Error;

    private WaveInEvent? _capture;
    private BufferedWaveProvider? _buffer;
    private MediaFoundationResampler? _resampler;
    private int _deviceNumber;
    private bool _running;
    private readonly List<short> _pending = [];

    public void Start()
    {
        if (_running)
            return;

        var tried = new List<string>();
        var defaultNumber = -1; // WAVE_MAPPER: whatever Windows points at
        foreach (var deviceNumber in DevicesToTry())
        {
            try
            {
                OpenDevice(deviceNumber);
                return;
            }
            catch (Exception ex)
            {
                tried.Add($"{Describe(deviceNumber, defaultNumber)}: {ex.Message}");
                Cleanup();
            }
        }

        Error?.Invoke(
            tried.Count == 0
                ? "No microphone device found."
                : $"Microphone could not start: {string.Join(" · ", tried)}");
    }

    private IEnumerable<int> DevicesToTry()
    {
        yield return -1;
        var count = WaveInEvent.DeviceCount;
        for (var i = 0; i < count; i++)
            yield return i;
    }

    private static string Describe(int deviceNumber, int defaultNumber)
    {
        if (deviceNumber == defaultNumber)
            return "default capture device";
        try
        {
            return WaveInEvent.GetCapabilities(deviceNumber).ProductName;
        }
        catch
        {
            return $"capture device #{deviceNumber}";
        }
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;
        Cleanup();
    }

    private void OpenDevice(int deviceNumber)
    {
        _deviceNumber = deviceNumber;
        _buffer = new BufferedWaveProvider(new WaveFormat(TargetRate, 16, channels: 1))
        {
            BufferDuration = TimeSpan.FromSeconds(4),
            DiscardOnBufferOverflow = true,
            ReadFully = false, // pull-contract: 0 bytes when caught up, never silence padding
        };
        _capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = _buffer.WaveFormat,
            BufferMilliseconds = 40,
        };
        _capture.DataAvailable += OnDataAvailable;
        _resampler = new MediaFoundationResampler(_buffer, new WaveFormat(TargetRate, 16, channels: 1))
        {
            ResamplerQuality = 60,
        };
        _capture.StartRecording();
        _running = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_buffer is null || _resampler is null)
            return;
        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        var resampled = new byte[TargetRate * 2 * 2]; // up to 200 ms per drain pass
        int read;
        while ((read = _resampler.Read(resampled, 0, resampled.Length)) > 0)
        {
            for (var i = 0; i + 1 < read; i += 2)
                _pending.Add((short)(resampled[i] | (resampled[i + 1] << 8)));

            while (_pending.Count >= FrameSamples)
            {
                var frameSamples = _pending.GetRange(0, FrameSamples).ToArray();
                _pending.RemoveRange(0, FrameSamples);
                Frame?.Invoke(new AudioFrame(frameSamples));
            }
        }
    }

    private void Cleanup()
    {
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
        _capture = null;
        try { _resampler?.Dispose(); } catch { }
        _resampler = null;
        _buffer = null;
        _pending.Clear();
    }

    public void Dispose() => Stop();
}
