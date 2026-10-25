using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cothon.Core.Phone;
using Cothon.Core.Settings;

namespace Cothon.Ui.ViewModels;

/// <summary>
/// Settings form: fields live in the view model; the store only changes on
/// Save, and status lines never leave the user guessing why something failed.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _stored;
    private readonly Func<string, string> _adbResolver;
    private readonly Func<string, string, int, IDeviceConnector> _phoneFactory;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _provider = "echo";

    [ObservableProperty]
    private string _geminiApiKey = "";

    [ObservableProperty]
    private bool _memoryLogging = true;

    [ObservableProperty]
    private string _hotkey = "Ctrl+Alt+Space";

    [ObservableProperty]
    private string _phoneHost = "";

    [ObservableProperty]
    private string _phonePort = "5555";

    [ObservableProperty]
    private string _adbPath = "";

    public SettingsViewModel()
        : this(AppSettings.Load(), DefaultAdbResolver, DefaultPhoneFactory)
    {
    }

    internal SettingsViewModel(
        AppSettings stored,
        Func<string, string>? adbResolver = null,
        Func<string, string, int, IDeviceConnector>? phoneFactory = null)
    {
        _stored = stored;
        _adbResolver = adbResolver ?? DefaultAdbResolver;
        _phoneFactory = phoneFactory ?? DefaultPhoneFactory;

        Provider = string.IsNullOrWhiteSpace(stored.ModelProvider) ? "echo" : stored.ModelProvider;
        GeminiApiKey = stored.GeminiApiKey;
        MemoryLogging = stored.MemoryLogging;
        Hotkey = stored.Hotkey;
        PhoneHost = stored.PhoneHost;
        PhonePort = stored.PhonePort.ToString();
        AdbPath = stored.AdbPath;
    }

    public string[] Providers { get; } = ["echo", "gemini"];

    private static string DefaultAdbResolver(string configuredPath)
        => AdbProbe.ResolveAdb(
            configuredPath,
            Environment.GetEnvironmentVariable("PATH") ?? "",
            AdbProbe.DefaultCommonDirs);

    private static IDeviceConnector DefaultPhoneFactory(string adbPath, string host, int port)
        => new AdbClient(adbPath, host, port);

    [RelayCommand]
    public void ProbeAdb()
    {
        var resolved = _adbResolver(AdbPath.Trim());
        Status = resolved.Length > 0
            ? $"adb found: {resolved}"
            : $"adb not found — install platform-tools, then set its path here. ({AdbProbe.MissingMessage()})";
    }

    [RelayCommand]
    public void Save()
    {
        _stored.ModelProvider = string.Equals(Provider, "gemini", StringComparison.OrdinalIgnoreCase)
            ? "gemini" : "echo";
        _stored.GeminiApiKey = GeminiApiKey.Trim();
        _stored.MemoryLogging = MemoryLogging;
        _stored.Hotkey = Hotkey.Trim();
        _stored.PhoneHost = PhoneHost.Trim();
        _stored.AdbPath = AdbPath.Trim();
        if (int.TryParse(PhonePort.Trim(), out var port) && port is > 0 and < 65536)
            _stored.PhonePort = port;

        _stored.Save();
        Status = "Saved. Changes apply on next launch.";
    }

    [RelayCommand]
    public async Task TestPhone()
    {
        var resolved = _adbResolver(AdbPath.Trim());
        if (resolved.Length == 0)
        {
            Status = $"adb not found — install platform-tools and set its path here. ({AdbProbe.MissingMessage()})";
            return;
        }

        var host = PhoneHost.Trim();
        if (host.Length == 0)
        {
            Status = "Set the phone address first, e.g. 192.168.1.20.";
            return;
        }

        if (!int.TryParse(PhonePort.Trim(), out var port) || port is <= 0 or >= 65536)
        {
            Status = "Port must be a number between 1 and 65535.";
            return;
        }

        var connector = _phoneFactory(resolved, host, port);
        var ok = await connector.ConnectAsync();
        Status = ok
            ? $"Connected to {host}:{port}."
            : $"Could not connect to {host}:{port}: {connector.LastError}";
    }
}
