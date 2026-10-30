using System.Diagnostics;
using System.Text;

namespace Cothon.Core.Phone;

/// <summary>
/// Locks every device command to the configured transport and parses the
/// device list. <c>connect</c>/<c>disconnect</c>/<c>devices</c> are global adb
/// commands and stay unpinned; anything that touches a specific phone must
/// carry <c>-s host:port</c>.
/// </summary>
public static class AdbTransport
{
    /// <summary>Builds the final adb argument list, pinning device commands.</summary>
    public static string[] BuildArguments(string[] args, string deviceId, bool targetDevice)
    {
        if (targetDevice && !string.IsNullOrEmpty(deviceId))
            return ["-s", deviceId, .. args];
        return args;
    }

    /// <summary>True when the given deviceId appears in `adb devices` output.</summary>
    public static bool DeviceListContains(string devicesOutput, string? deviceId)
    {
        foreach (var line in devicesOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith("List of", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
                continue;

            var parts = t.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[1].StartsWith("device", StringComparison.Ordinal))
                continue;

            if (string.IsNullOrEmpty(deviceId) || string.Equals(parts[0], deviceId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

/// <summary>
/// The adb command runner: missing binary, nonzero exit and stderr errors
/// become <see cref="AdbOutcome.Failed"/> — an empty result is never success.
/// Shell pipelines are exempt from exit-code checks because grep/find exit
/// nonzero when they simply find nothing.
/// </summary>
public class AdbClient(string adbPath, string host, int port = 5555, string? serial = null) : IDeviceConnector, IPhonePusher
{
    private readonly string _deviceId = string.IsNullOrWhiteSpace(host) ? (serial ?? "") : $"{host}:{port}";

    public string DeviceId => _deviceId;

    public string Host => host;

    public int Port => port;

    public string LastError { get; protected set; } = "";

    /// <summary>Copies a PC file to the phone (adb push, 30 s budget).</summary>
    public virtual async Task<AdbOutcome> PushAsync(string local, string remote, CancellationToken ct = default)
    {
        var outcome = await RunAsync(["push", local, remote], timeoutSeconds: 30, ct: ct).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>Runs adb and returns stdout; sets LastError on any failure.</summary>
    public async Task<AdbOutcome> RunAsync(string[] args, int timeoutSeconds = 15, bool targetDevice = true, CancellationToken ct = default)
    {
        LastError = "";
        if (!File.Exists(adbPath))
        {
            LastError = $"adb not found at {adbPath}. Install platform-tools and set the adb path.";
            return AdbOutcome.Failure;
        }

        var pinned = AdbTransport.BuildArguments(args, _deviceId, targetDevice);

        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var a in pinned) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                LastError = $"Could not start adb ({adbPath}).";
                return AdbOutcome.Failure;
            }

            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);

            var exited = await Task.Run(() => proc.WaitForExit(timeoutSeconds * 1000), ct).ConfigureAwait(false);
            if (!exited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                LastError = $"adb timed out after {timeoutSeconds}s: {string.Join(' ', args)}";
                return AdbOutcome.Failure;
            }

            var o = (await stdout.ConfigureAwait(false)).Trim();
            var e = (await stderr.ConfigureAwait(false)).Trim();

            var piped = args.Any(a => a.Contains('|'));
            if (proc.ExitCode != 0 && !piped)
            {
                LastError = string.IsNullOrWhiteSpace(e)
                    ? $"adb exited with code {proc.ExitCode}: {string.Join(' ', args)}"
                    : e;
                return AdbOutcome.Ok(o);
            }

            if (o.Length == 0 && e.Length > 0)
            {
                LastError = e;
                return AdbOutcome.Failure;
            }
            return AdbOutcome.Ok(o.Length == 0 ? e : o);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = $"adb invocation failed: {ex.Message}";
            return AdbOutcome.Failure;
        }
    }

    /// <summary>Connects to the configured transport and verifies it answers.</summary>
    public virtual async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        var devices = await RunAsync(["devices"], targetDevice: false, ct: ct).ConfigureAwait(false);
        if (devices.IsFailed) return false;
        if (AdbTransport.DeviceListContains(devices.Stdout, _deviceId)) return true;

        var connect = await RunAsync(["connect", _deviceId], timeoutSeconds: 8, targetDevice: false, ct: ct).ConfigureAwait(false);
        return !connect.IsFailed && connect.Stdout.Contains("connected", StringComparison.OrdinalIgnoreCase)
               && AdbTransport.DeviceListContains((await RunAsync(["devices"], targetDevice: false, ct: ct).ConfigureAwait(false)).Stdout, _deviceId);
    }
}

public sealed record AdbOutcome(bool IsFailed, string Stdout)
{
    public static AdbOutcome Ok(string stdout) => new(false, stdout);
    public static AdbOutcome Failure { get; } = new(true, "");
}

/// <summary>
/// Argument quoting for commands sent to <c>adb shell</c>. Values are pasted
/// into a shell on the phone; single-quoting with escaped embedded quotes
/// makes them inert. <c>input text</c> takes one argument, so spaces become %s.
/// </summary>
public static class AdbShell
{
    public static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "''";
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    public static string QuoteInputText(string? value) =>
        Quote((value ?? "").Replace(" ", "%s"));
}

/// <summary>
/// Locates adb: configured path → PATH → common install dirs. Pure in its
/// inputs so tests run without a real PATH or device.
/// </summary>
public static class AdbProbe
{
    public static readonly string[] DefaultCommonDirs =
    [
        @"C:\platform-tools",
        @"C:\adb",
        @"C:\Android\platform-tools",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools"),
    ];

    public static string ResolveAdb(string configuredPath, string pathEnv, IReadOnlyList<string> commonDirs)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        foreach (var dir in (pathEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        foreach (var name in ExecutableNames())
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }

        foreach (var dir in commonDirs)
        foreach (var name in ExecutableNames())
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }

        return "";
    }

    public static string MissingMessage() =>
        "adb was not found. Install Android platform-tools (developer.android.com/tools/releases/platform-tools) " +
        "and either add adb.exe to PATH or set its path in settings.";

    private static string[] ExecutableNames() =>
        OperatingSystem.IsWindows() ? ["adb.exe", "adb.bat", "adb"] : ["adb"];
}
