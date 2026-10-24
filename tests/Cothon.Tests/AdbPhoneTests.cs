using Cothon.Core.Phone;
using Xunit;

public class AdbTransportTests
{
    [Theory]
    [InlineData(new[] { "shell", "wm size" }, "192.168.1.9:5555", true, new[] { "-s", "192.168.1.9:5555", "shell", "wm size" })]
    [InlineData(new[] { "shell", "wm size" }, "", true, new[] { "shell", "wm size" })]
    [InlineData(new[] { "connect", "h:5555" }, "h:5555", false, new[] { "connect", "h:5555" })]
    public void BuildArguments_pins_device_commands_only(string[] args, string deviceId, bool targetDevice, string[] expected)
    {
        Assert.Equal(expected, AdbTransport.BuildArguments(args, deviceId, targetDevice));
    }

    [Theory]
    [InlineData("List of devices attached\n192.168.1.9:5555\tdevice\n", "192.168.1.9:5555", true)]
    [InlineData("List of devices attached\n192.168.1.9:5555\tdevice\n", "10.0.0.7:5555", false)]
    [InlineData("List of devices attached\n192.168.1.9:5555\toffline\n", "192.168.1.9:5555", false)]
    [InlineData("* daemon not running; starting now at tcp:5037\nList of devices attached\nserial1\tdevice\n", "serial1", true)]
    [InlineData("List of devices attached\n", "anything", false)]
    public void DeviceListContains_parses_adb_devices_output(string output, string deviceId, bool expected)
    {
        Assert.Equal(expected, AdbTransport.DeviceListContains(output, deviceId));
    }
}

public class AdbShellQuotingTests
{
    [Fact]
    public void Empty_and_null_become_empty_quotes()
    {
        Assert.Equal("''", AdbShell.Quote(null));
        Assert.Equal("''", AdbShell.Quote(""));
    }

    [Fact]
    public void Embedded_quote_cannot_escape()
    {
        Assert.Equal("'don'\\''t'", AdbShell.Quote("don't"));
        Assert.Equal("'a;b|rm'", AdbShell.Quote("a;b|rm"));
    }

    [Fact]
    public void Input_text_encodes_spaces_as_percent_s()
    {
        Assert.Equal("'hello%s%sworld'", AdbShell.QuoteInputText("hello  world"));
    }
}

public class AdbProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cothon-probe-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string MakeAdb(string name)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "pretend adb");
        return path;
    }

    [Fact]
    public void Configure_path_wins_when_it_exists()
    {
        var adb = MakeAdb("adb.exe");
        Assert.Equal(adb, AdbProbe.ResolveAdb(adb, "", AdbProbe.DefaultCommonDirs));
    }

    [Fact]
    public void Path_entries_are_searched_when_not_configured()
    {
        var adb = MakeAdb("adb.exe");
        Assert.Equal(adb, AdbProbe.ResolveAdb("", _dir, []));
    }

    [Fact]
    public void Common_dirs_are_the_last_resort()
    {
        var adb = MakeAdb("adb.exe");
        Assert.Equal(adb, AdbProbe.ResolveAdb("", "", [_dir]));
    }

    [Fact]
    public void Nothing_found_yields_empty_string()
    {
        Assert.Equal("", AdbProbe.ResolveAdb(@"C:\gone\adb.exe", @"C:\gone2", [_dir]));
    }
}
