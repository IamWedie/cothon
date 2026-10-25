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

        Assert.Equal("", stored.PhoneHost); // untouched before Save
        Assert.Equal("Ctrl+Alt+Space", stored.Hotkey);
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
        vm.Provider = "gemini";
        vm.SaveCommand.Execute(null);

        Assert.Equal("192.168.1.20", stored.PhoneHost);
        Assert.Equal(@"C:\platform-tools\adb.exe", stored.AdbPath);
        Assert.Equal("F9", stored.Hotkey);
        Assert.Equal("gemini", stored.ModelProvider);
        Assert.Contains("Saved", vm.Status);
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
}
