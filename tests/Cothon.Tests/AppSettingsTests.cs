using Cothon.Core.Settings;
using Xunit;

[CollectionDefinition("KeyFile", DisableParallelization = true)]
public sealed class KeyFileCollection;

[Collection(nameof(KeyFileCollection))]
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
        Assert.Equal("light", settings.Appearance);
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
            Appearance = "system",
        };
        settings.Save();

        var loaded = AppSettings.Load();
        Assert.Equal("F9", loaded.Hotkey);
        Assert.Equal("192.168.1.20", loaded.PhoneHost);
        Assert.Equal(5556, loaded.PhonePort);
        Assert.Equal(@"C:\platform-tools\adb.exe", loaded.AdbPath);
        Assert.Equal("gemini", loaded.ModelProvider);
        Assert.Equal("system", loaded.Appearance);
    }

    [Fact]
    public void Unknown_appearance_falls_back_to_light()
    {
        Assert.Equal("light", new AppSettings { Appearance = "banana" }.Appearance);
    }

    [Fact]
    public void Save_does_not_write_plaintext_keys()
    {
        var settings = new AppSettings { GeminiApiKey = "secret-gemini-77" };
        settings.Save();

        var onDisk = File.ReadAllText(Path.Combine(AppSettings.Folder, "settings.json"));

        Assert.DoesNotContain("secret-gemini-77", onDisk);
        Assert.Contains("GeminiKeyCipher", onDisk);
    }

    [Fact]
    public void Round_trip_ciphers_decrypt_to_plaintext()
    {
        var settings = new AppSettings
        {
            GeminiApiKey = "g-key",
            OpenAiApiKey = "o-key",
            AnthropicApiKey = "c-key",
        };
        settings.Save();

        var loaded = AppSettings.Load();
        Assert.Equal("g-key", loaded.GeminiApiKey);
        Assert.Equal("o-key", loaded.OpenAiApiKey);
        Assert.Equal("c-key", loaded.AnthropicApiKey);
    }

    [Fact]
    public void Empty_keys_stay_empty_without_errors()
    {
        var settings = new AppSettings();
        settings.Save();

        var loaded = AppSettings.Load();

        Assert.Equal("", loaded.GeminiApiKey);
        Assert.Equal("", loaded.OpenAiApiKey);
        Assert.Equal("", loaded.AnthropicApiKey);
    }

    [Fact]
    public void Legacy_plaintext_file_is_migrated()
    {
        var legacyJson = """
        {
          "ModelProvider": "gemini",
          "GeminiApiKey": "plain-legacy-key",
          "MemoryLogging": true
        }
        """;
        Directory.CreateDirectory(AppSettings.Folder);
        File.WriteAllText(Path.Combine(AppSettings.Folder, "settings.json"), legacyJson);

        var loaded = AppSettings.Load();

        Assert.Equal("plain-legacy-key", loaded.GeminiApiKey);

        var onDisk = File.ReadAllText(Path.Combine(AppSettings.Folder, "settings.json"));
        Assert.DoesNotContain("plain-legacy-key", onDisk);
        Assert.Contains("GeminiKeyCipher", onDisk);
    }

    [Fact]
    public void Corrupt_cipher_falls_back_to_empty()
    {
        var tampered = """{ "GeminiKeyCipher": "!!!not base64!!!" }""";
        Directory.CreateDirectory(AppSettings.Folder);
        File.WriteAllText(Path.Combine(AppSettings.Folder, "settings.json"), tampered);

        var loaded = AppSettings.Load();

        Assert.Equal("", loaded.GeminiApiKey);
    }
}