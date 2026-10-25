using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cothon.Core.Settings;

/// <summary>User preferences persisted to %LOCALAPPDATA%\Cothon\settings.json.</summary>
public sealed class AppSettings
{
    public string ModelProvider { get; set; } = "echo";

    /// <summary>Lives in-process only; the disk copy is a DPAPI cipher.</summary>
    [JsonIgnore]
    public string GeminiApiKey { get; set; } = "";
    [JsonIgnore]
    public string OpenAiApiKey { get; set; } = "";
    [JsonIgnore]
    public string AnthropicApiKey { get; set; } = "";

    /// <summary>Base64 DPAPI blob; empty string when the key is unset.</summary>
    public string GeminiKeyCipher { get; set; } = "";
    public string OpenAiKeyCipher { get; set; } = "";
    public string AnthropicKeyCipher { get; set; } = "";

    /// <summary>Syncs plaintext keys into ciphers (runtime → disk view).</summary>
    private void ProtectKeys()
    {
        GeminiKeyCipher = GeminiApiKey.Length > 0 ? KeyVault.Protect(GeminiApiKey) : "";
        OpenAiKeyCipher = OpenAiApiKey.Length > 0 ? KeyVault.Protect(OpenAiApiKey) : "";
        AnthropicKeyCipher = AnthropicApiKey.Length > 0 ? KeyVault.Protect(AnthropicApiKey) : "";
    }

    /// <summary>Decrypts ciphers into runtime plaintext (disk view → runtime).</summary>
    private void UnprotectKeys()
    {
        GeminiApiKey = KeyVault.Unprotect(GeminiKeyCipher);
        OpenAiApiKey = KeyVault.Unprotect(OpenAiKeyCipher);
        AnthropicApiKey = KeyVault.Unprotect(AnthropicKeyCipher);
    }

    /// <summary>
    /// One-way migration: settings.json written by an older build holds the
    /// keys in plaintext. Adopt them, seal them, and rewrite the file at once
    /// so the plaintext never outlives this load.
    /// </summary>
    private void SealLegacyKeys(JsonElement root)
    {
        var migrated = false;
        migrated |= TrySeal(root, "GeminiApiKey", g => GeminiApiKey = g);
        migrated |= TrySeal(root, "OpenAiApiKey", k => OpenAiApiKey = k);
        migrated |= TrySeal(root, "AnthropicApiKey", c => AnthropicApiKey = c);
        if (migrated)
        {
            ProtectKeys();
            Save();
        }
    }

    private bool TrySeal(JsonElement root, string property, Action<string> adopt)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        var plain = value.GetString() ?? "";
        if (plain.Length == 0)
            return false;
        adopt(plain);
        return true;
    }

    public bool MemoryLogging { get; set; } = true;
    public string SandboxRoot { get; set; } = "sandbox";
    public bool CompactMode { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public string PhoneHost { get; set; } = "";
    public int PhonePort { get; set; } = 5555;
    public string AdbPath { get; set; } = "";
    /// <summary>Screen awareness: capture on each message (user-requested).</summary>
    public bool ScreenCapture { get; set; } = true;

    /// <summary>Speak the final answer aloud through Gemini TTS.</summary>
    public bool SpeechEnabled { get; set; } = true;

    /// <summary>External MCP servers conferring extra tools.</summary>
    public List<McpServerConfig> McpServers { get; set; } = [];

    /// <summary>light | dark | system (case-insensitive; anything else is light).</summary>
    public string Appearance
    {
        get => _appearance;
        set => _appearance = IsAppearance(value) ? value.ToLowerInvariant() : "light";
    }
    private string _appearance = "light";

    public static readonly string[] AppearanceChoices = ["light", "dark", "system"];

    private static bool IsAppearance(string? value)
        => value is not null && AppearanceChoices.Contains(value.Trim().ToLowerInvariant());

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cothon");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Load/Save share one file; tests and windows race otherwise.</summary>
    private static readonly object FileGate = new();

    public static AppSettings Load()
    {
        try
        {
            lock (FileGate)
            {
                if (File.Exists(FilePath))
                {
                    var raw = File.ReadAllText(FilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(raw) ?? new AppSettings();
                    settings.UnprotectKeys();
                    using var doc = JsonDocument.Parse(raw);
                    settings.SealLegacyKeys(doc.RootElement);
                    return settings;
                }
            }
        }
        catch (JsonException)
        {
            // A corrupted settings file must not be fatal: start fresh.
        }
        return new AppSettings();
    }

    public void Save()
    {
        ProtectKeys();
        Directory.CreateDirectory(Folder);
        lock (FileGate)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}