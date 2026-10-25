using System.Text.Json;

namespace Cothon.Core.Settings;

/// <summary>User preferences persisted to %LOCALAPPDATA%\Cothon\settings.json.</summary>
public sealed class AppSettings
{
    public string ModelProvider { get; set; } = "echo";
    public string GeminiApiKey { get; set; } = "";
    public string OpenAiApiKey { get; set; } = "";
    public string AnthropicApiKey { get; set; } = "";
    public bool MemoryLogging { get; set; } = true;
    public string SandboxRoot { get; set; } = "sandbox";
    public bool CompactMode { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public string PhoneHost { get; set; } = "";
    public int PhonePort { get; set; } = 5555;
    public string AdbPath { get; set; } = "";
    /// <summary>Screen awareness: capture on each message (user-requested).</summary>
    public bool ScreenCapture { get; set; } = true;

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
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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
        Directory.CreateDirectory(Folder);
        lock (FileGate)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}