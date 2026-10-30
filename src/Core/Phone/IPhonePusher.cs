namespace Cothon.Core.Phone;

/// <summary>
/// Pushes a PC file over a device transport. Kept apart from
/// <see cref="AdbClient"/> so the healing wrapper can stand in for it
/// without the tool knowing the difference.
/// </summary>
public interface IPhonePusher
{
    string LastError { get; }

    Task<AdbOutcome> PushAsync(string local, string remote, CancellationToken ct = default);
}
