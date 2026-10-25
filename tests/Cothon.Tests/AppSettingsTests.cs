using Cothon.Core.Settings;
using Xunit;

public class AppSettingsTests
{
    [Fact]
    public void Load_when_no_file_returns_defaults()
    {
        var settings = AppSettings.Load();
        Assert.False(string.IsNullOrEmpty(settings.ModelProvider));
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var settings = new AppSettings { GeminiApiKey = "test-key-not-real", MemoryLogging = false };
        settings.Save();

        var loaded = AppSettings.Load();
        Assert.Equal("test-key-not-real", loaded.GeminiApiKey);
        Assert.False(loaded.MemoryLogging);
    }

    [Fact]
    public void New_fields_have_sane_defaults()
    {
        var settings = new AppSettings();

        Assert.Equal("Ctrl+Alt+Space", settings.Hotkey);
        Assert.Equal("", settings.PhoneHost);
        Assert.Equal(5555, settings.PhonePort);
        Assert.Equal("", settings.AdbPath);
        Assert.True(settings.ScreenCapture);
    }

    [Fact]
    public void Save_then_Load_round_trips_new_fields()
    {
        var settings = new AppSettings
        {
            Hotkey = "F9",
            PhoneHost = "192.168.1.20",
            PhonePort = 5556,
            AdbPath = @"C:\platform-tools\adb.exe",
            ModelProvider = "gemini",
        };
        settings.Save();

        var loaded = AppSettings.Load();
        Assert.Equal("F9", loaded.Hotkey);
        Assert.Equal("192.168.1.20", loaded.PhoneHost);
        Assert.Equal(5556, loaded.PhonePort);
        Assert.Equal(@"C:\platform-tools\adb.exe", loaded.AdbPath);
        Assert.Equal("gemini", loaded.ModelProvider);
    }
}