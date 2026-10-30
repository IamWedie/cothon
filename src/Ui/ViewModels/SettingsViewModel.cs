using Avalonia;
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
    private Avalonia.Media.IImage? _pairQr;

    [ObservableProperty]
    private string _pairStatus = "";

    [ObservableProperty]
    private bool _pairRunning;

    [ObservableProperty]
    private string _provider = "echo";

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private bool _memoryLogging = true;

    [ObservableProperty]
    private bool _screenCapture = true;

    [ObservableProperty]
    private string _appearance = "light";

    [ObservableProperty]
    private string _hotkey = "Ctrl+Alt+Space";

    [ObservableProperty]
    private string _pillHotkey = "Ctrl+Alt+P";

    [ObservableProperty]
    private string _phoneHost = "";

    [ObservableProperty]
    private string _phonePort = "5555";

    [ObservableProperty]
    private string _adbPath = "";

    [ObservableProperty]
    private string _providerCaption = "";

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
        PairWindowOpener = () => PairWindow.Open(this).Invoke();

        Provider = string.IsNullOrWhiteSpace(stored.ModelProvider) ? "echo" : stored.ModelProvider;
        ApiKey = KeyFor(stored, Provider);
        MemoryLogging = stored.MemoryLogging;
        ScreenCapture = stored.ScreenCapture;
        Appearance = stored.Appearance;
        Hotkey = stored.Hotkey;
        PillHotkey = stored.PillHotkey;
        PhoneHost = stored.PhoneHost;
        PhonePort = stored.PhonePort.ToString();
        AdbPath = stored.AdbPath;
        ProviderCaption = Caption();
    }

    public string[] Providers { get; } = ["echo", "gemini", "openai", "claude"];

    public string[] AppearanceChoices { get; } = ["light", "dark", "system"];

    partial void OnProviderChanged(string value) => ProviderCaption = Caption();

    /// <summary>Theme is the one preference that never waits for launch.</summary>
    partial void OnAppearanceChanged(string value)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = UiAppearance.Resolve(value);
    }

    private string Caption() => Provider switch
    {
        "gemini" => "Google Gemini — text and realtime voice (Gemini Live); screen reading included.",
        "openai" => "OpenAI ChatGPT — text with images; realtime voice via the Realtime API.",
        "claude" => "Anthropic Claude — text with images; voice rides on cloud speech, not native yet.",
        _ => "Echo is a local test provider: repeats your text, no network, no voice.",
    };

    private static string KeyFor(AppSettings stored, string provider) => provider switch
    {
        "gemini" => stored.GeminiApiKey,
        "openai" => stored.OpenAiApiKey,
        "claude" => stored.AnthropicApiKey,
        _ => "",
    };

    private static string DefaultAdbResolver(string configuredPath)
        => AdbProbe.ResolveAdb(
            configuredPath,
            Environment.GetEnvironmentVariable("PATH") ?? "",
            AdbProbe.DefaultCommonDirs);

    private static IDeviceConnector DefaultPhoneFactory(string adbPath, string host, int port)
        => new AdbClient(adbPath, host, port);

    /// <summary>Opens the QR dialog. Tests keep this null. Dragged out of a Func trap: it must be a plain call.</summary>
    internal Action? PairWindowOpener { get; set; }

    /// <summary>Pairing seam: tests pin a canned outcome, production wires the real mDNS pairer once adb resolves.</summary>
    internal Func<PhonePairingSecret, Task<PhonePairOutcome>>? PairAsync { get; set; }

    /// <summary>QR renderer seam: production uses Avalonia bitmaps, tests use a stub.</summary>
    internal Func<string, Avalonia.Media.IImage?>? QrRender { get; set; } = payload => QrImage.Render(payload);

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
        var provider = Provider.Trim().ToLowerInvariant() switch
        {
            "gemini" or "openai" or "claude" => Provider.Trim().ToLowerInvariant(),
            _ => "echo",
        };

        _stored.ModelProvider = provider;
        switch (provider)
        {
            case "gemini": _stored.GeminiApiKey = ApiKey.Trim(); break;
            case "openai": _stored.OpenAiApiKey = ApiKey.Trim(); break;
            case "claude": _stored.AnthropicApiKey = ApiKey.Trim(); break;
        }

        _stored.MemoryLogging = MemoryLogging;
        _stored.ScreenCapture = ScreenCapture;
        _stored.Appearance = Appearance.Trim().ToLowerInvariant();
        _stored.Hotkey = Hotkey.Trim();
        _stored.PillHotkey = PillHotkey.Trim();
        _stored.PhoneHost = PhoneHost.Trim();
        _stored.AdbPath = AdbPath.Trim();
        if (int.TryParse(PhonePort.Trim(), out var port) && port is > 0 and < 65536)
            _stored.PhonePort = port;

        _stored.Save();
        Status = "Saved — theme is already applied, the rest applies on next launch.";
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

    /// <summary>
    /// QR pairing: render the one-time secret, let the phone scan it, poll
    /// mDNS for its pairing service, seal the pairing and hold the live
    /// endpoint for next launch. Nothing silent: every step says where we are.
    /// </summary>
    [RelayCommand]
    public async Task PairWithQrAsync()
    {
        var adbPath = _adbResolver(AdbPath.Trim());
        if (adbPath.Length == 0)
        {
            Status = $"adb is not set — install platform-tools first. ({AdbProbe.MissingMessage()})";
            return;
        }

        if (PairAsync is null)
        {
            var adb = new AdbClient(adbPath, HostIfAny(), PortIfAny());
            PairAsync = secret => PhoneQrPairer.Production(adb).PairByQrAsync(secret, CancellationToken.None);
        }

        PairRunning = true;
        PairStatus = "Show this QR to your phone: Settings > Developer options > Wireless debugging > “Pair device with QR code”. Keep both on the same Wi-Fi.";
        PairWindowOpener?.Invoke();

        PhonePairingSecret generated;
        try
        {
            generated = PhonePairingSecret.Generate();
            PairQr = QrRender?.Invoke(generated.Payload);
        }
        catch (Exception ex)
        {
            PairStatus = $"Could not draw the QR code: {ex.Message} — the dialog is still open; tell me this line and I will fix it.";
            PairRunning = false;
            return;
        }

        PhonePairOutcome outcome;
        try
        {
            outcome = await PairAsync(generated);
        }
        catch (Exception ex)
        {
            outcome = new PhonePairOutcome(false, ex.Message);
        }
        finally
        {
            PairRunning = false;
        }

        PairStatus = outcome.Paired
            ? outcome.ConnectHost.Length > 0
                ? $"Paired. Phone address held for next launch: {outcome.ConnectHost}:{outcome.ConnectPort}"
                : outcome.Note
            : outcome.Note;

        if (outcome.Paired && outcome.ConnectHost.Length > 0)
        {
            PhoneHost = outcome.ConnectHost;
            PhonePort = outcome.ConnectPort.ToString();
            _stored.PhoneHost = outcome.ConnectHost;
            _stored.PhonePort = outcome.ConnectPort;
            _stored.Save();
        }
    }

    private string HostIfAny() => PhoneHost.Trim();
    private int PortIfAny() => int.TryParse(PhonePort.Trim(), out var port) ? port : 5555;
}
