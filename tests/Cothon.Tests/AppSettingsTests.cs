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
}