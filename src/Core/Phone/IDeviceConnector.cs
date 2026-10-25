namespace Cothon.Core.Phone;

/// <summary>
/// A phone transport seam so Settings (and every future tool) can probe a
/// device without spawning real adb in tests.
/// </summary>
public interface IDeviceConnector
{
    string LastError { get; }

    Task<bool> ConnectAsync(CancellationToken ct = default);
}
