using Cothon.Core.Phone;
using Cothon.Core.Settings;
using Cothon.Ui.ViewModels;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// Settings: values land on the store only on Save, adb probing speaks in
/// plain sentences, and the phone test surfaces the real error.
/// </summary>
public sealed class SettingsViewModelTests
{
    private static IDeviceConnector Connector(bool ok) => new StubConnector(ok);

    private sealed class StubConnector(bool ok) : IDeviceConnector
    {
        public string LastError { get; } = ok ? "" : "connection refused";
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(ok);
    }

    [Fact]
    public void Store_untouched_before_save()
    {
        var stored = new AppSettings();
        var vm = new SettingsViewModel(
            stored, adbResolver: _ => @"C:\fake\adb.exe", phoneFactory: (_, _, _) => Connector(true));

        vm.PhoneHost = "192.168.1.20";
        vm.Hotkey = "F9";
        vm.PillHotkey = "Ctrl+Shift+K";

        Assert.Equal("", stored.PhoneHost); // untouched before Save
        Assert.Equal("Ctrl+Alt+Space", stored.Hotkey);
        Assert.Equal("Ctrl+Alt+P", stored.PillHotkey);
    }

    [Fact]
    public void Pill_hotkey_loads_with_the_store()
    {
        var stored = new AppSettings { PillHotkey = "Ctrl+Shift+K" };
        var vm = new SettingsViewModel(
            stored, adbResolver: _ => "", phoneFactory: (_, _, _) => Connector(true));

        Assert.Equal("Ctrl+Shift+K", vm.PillHotkey);
    }

    [Fact]
    public void Appearance_choices_cover_all_three_modes()
    {
        var vm = new SettingsViewModel(new AppSettings());

        Assert.Equal(["light", "dark", "system"], vm.AppearanceChoices);
    }

    [Fact]
    public void Save_pushes_all_fields_into_store()
    {
        var stored = new AppSettings();
        var vm = new SettingsViewModel(
            stored, adbResolver: _ => @"C:\fake\adb.exe", phoneFactory: (_, _, _) => Connector(true));

        vm.PhoneHost = "192.168.1.20";
        vm.PhonePort = "5555";
        vm.AdbPath = @"C:\platform-tools\adb.exe";
        vm.Hotkey = "F9";
        vm.PillHotkey = "Ctrl+Shift+K";
        vm.Provider = "gemini";
        vm.ApiKey = "g-key";
        vm.Appearance = "dark";
        vm.SaveCommand.Execute(null);

        Assert.Equal("192.168.1.20", stored.PhoneHost);
        Assert.Equal(@"C:\platform-tools\adb.exe", stored.AdbPath);
        Assert.Equal("F9", stored.Hotkey);
        Assert.Equal("Ctrl+Shift+K", stored.PillHotkey);
        Assert.Equal("gemini", stored.ModelProvider);
        Assert.Equal("g-key", stored.GeminiApiKey);
        Assert.Equal("dark", stored.Appearance);
        Assert.Contains("Saved", vm.Status);
    }

    [Fact]
    public void Key_isStored_per_provider()
    {
        var stored = new AppSettings();
        var vm = new SettingsViewModel(
            stored, adbResolver: _ => "", phoneFactory: (_, _, _) => Connector(true));

        vm.Provider = "claude";
        vm.ApiKey = "c-key";
        vm.SaveCommand.Execute(null);

        Assert.Equal("c-key", stored.AnthropicApiKey);
        Assert.Equal("", stored.GeminiApiKey);

        var vm2 = new SettingsViewModel(stored);
        Assert.Equal("claude", vm2.Provider);
        Assert.Equal("c-key", vm2.ApiKey); // the selected provider's key round-trips
    }

    [Fact]
    public void Provider_captions_explain_capabilities_and_voice()
    {
        var vm = new SettingsViewModel(new AppSettings());

        foreach (var provider in vm.Providers)
        {
            vm.Provider = provider;
            Assert.False(string.IsNullOrWhiteSpace(vm.ProviderCaption), provider);
        }

        vm.Provider = "gemini";
        Assert.Contains("voice", vm.ProviderCaption);
        vm.Provider = "echo";
        Assert.Contains("test", vm.ProviderCaption);
    }

    [Fact]
    public void Probe_reports_missing_adb_in_plain_sentences()
    {
        var vm = new SettingsViewModel(
            new AppSettings(), adbResolver: _ => "", phoneFactory: (_, _, _) => Connector(true));

        vm.ProbeAdb();

        Assert.Contains("adb not found", vm.Status);
    }

    [Fact]
    public void Probe_reports_resolved_path()
    {
        var vm = new SettingsViewModel(
            new AppSettings(),
            adbResolver: _ => @"C:\platform-tools\adb.exe",
            phoneFactory: (_, _, _) => Connector(true));

        vm.ProbeAdb();

        Assert.Contains(@"C:\platform-tools\adb.exe", vm.Status);
    }

    [Fact]
    public async Task Phone_test_speaks_success_and_failure()
    {
        var ok = new SettingsViewModel(
            new AppSettings { PhoneHost = "10.0.0.5", PhonePort = 5555 },
            adbResolver: _ => @"C:\adb.exe",
            phoneFactory: (path, host, port) =>
            {
                Assert.Equal(@"C:\adb.exe", path);
                Assert.Equal("10.0.0.5", host);
                Assert.Equal(5555, port);
                return Connector(true);
            });
        await ok.TestPhone();
        Assert.Contains("Connected to 10.0.0.5:5555", ok.Status);

        var bad = new SettingsViewModel(
            new AppSettings { PhoneHost = "10.0.0.5" },
            adbResolver: _ => @"C:\adb.exe",
            phoneFactory: (_, _, _) => Connector(false));
        await bad.TestPhone();
        Assert.Contains("connection refused", bad.Status);
    }

    [Fact]
    public async Task Phone_test_without_adb_never_calls_the_factory()
    {
        var called = false;
        var vm = new SettingsViewModel(
            new AppSettings { PhoneHost = "10.0.0.5" },
            adbResolver: _ => "",
            phoneFactory: (_, _, _) => { called = true; return Connector(true); });

        await vm.TestPhone();

        Assert.False(called);
        Assert.Contains("adb not found", vm.Status);
    }

    [Fact]
    public async Task Phone_test_requires_a_host_and_valid_port()
    {
        var noHost = new SettingsViewModel(
            new AppSettings(),
            adbResolver: _ => @"C:\adb.exe",
            phoneFactory: (_, _, _) => Connector(true));
        await noHost.TestPhone();
        Assert.Contains("phone address", noHost.Status);

        var badPort = new SettingsViewModel(
            new AppSettings { PhoneHost = "10.0.0.5", PhonePort = 70000 },
            adbResolver: _ => @"C:\adb.exe",
            phoneFactory: (_, _, _) => Connector(true));
        await badPort.TestPhone();
        Assert.Contains("Port must be", badPort.Status);
    }

    private sealed class FakeQr : Avalonia.Media.IImage
    {
        public Avalonia.Size Size => new(8, 8);
        public void Draw(Avalonia.Media.DrawingContext context, Avalonia.Rect sourceRect, Avalonia.Rect destinationRect) { }
    }

    [Fact]
    public async Task Pairing_actually_opens_the_dialog_for_the_user()
    {
        var opened = 0;
        var vm = new SettingsViewModel(
            new AppSettings(),
            adbResolver: _ => @"C:\adb.exe",
            phoneFactory: (_, _, _) => Connector(true))
        {
            PairWindowOpener = () => opened++,
            PairAsync = secret => Task.FromResult(
                new PhonePairOutcome(false, "pause here")),
            QrRender = _ => new FakeQr(),
        };

        await vm.PairWithQrAsync();

        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task Qr_pairing_wins_and_persists_the_phone_endpoint()
    {
        var stored = new AppSettings { PhoneHost = "192.168.1.20", PhonePort = 5556 };
        var vm = new SettingsViewModel(
            stored,
            adbResolver: _ => @"C:\adb.exe",
            phoneFactory: (_, _, _) => Connector(true))
        {
            PairWindowOpener = () => { },
            PairAsync = secret => Task.FromResult(
                new PhonePairOutcome(true, "Paired and connected.", "192.168.1.127", 43475)),
            QrRender = _ => new FakeQr(),
        };

        await vm.PairWithQrAsync();

        Assert.NotNull(vm.PairQr);
        Assert.Contains("Paired. Phone address held", vm.PairStatus);
        Assert.Contains("192.168.1.127", vm.PairStatus);
        Assert.Equal("192.168.1.127", vm.PhoneHost);
        Assert.Equal("43475", vm.PhonePort);
        Assert.Equal("192.168.1.127", stored.PhoneHost);
        Assert.Equal(43475, stored.PhonePort);
        Assert.False(vm.PairRunning);
    }

    [Fact]
    public async Task Qr_pairing_refusal_is_spoken_and_storage_stays_put()
    {
        var stored = new AppSettings { PhoneHost = "192.168.1.20", PhonePort = 5556 };
        var vm = new SettingsViewModel(
            stored,
            adbResolver: _ => @"C:\adb.exe",
            phoneFactory: (_, _, _) => Connector(true))
        {
            PairWindowOpener = () => { },
            PairAsync = secret => Task.FromResult(
                new PhonePairOutcome(false, "Pairing was refused: protocol failed: 4099")),
            QrRender = _ => null,
        };

        await vm.PairWithQrAsync();

        Assert.Contains("4099", vm.PairStatus);
        Assert.Equal("192.168.1.20", stored.PhoneHost);
        Assert.Equal(5556, stored.PhonePort);
    }

    [Fact]
    public async Task Qr_pairing_never_when_adb_missing()
    {
        var vm = new SettingsViewModel(new AppSettings(), adbResolver: _ => "")
        {
            PairWindowOpener = () => { },
            PairAsync = secret => throw new InvalidOperationException("must not pair without adb"),
        };

        await vm.PairWithQrAsync();

        Assert.Null(vm.PairQr);
        Assert.Contains("adb", vm.Status);
    }
}
