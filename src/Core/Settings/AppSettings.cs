using System.Text.Json;

namespace Cothon.Core.Settings;

/// <summary>User preferences persisted to %LOCALAPPDATA%\Cothon\settings.json.</summary>
public sealed class AppSettings
{
    public string ModelProvider { get; set; } = "echo";
    public string GeminiApiKey { get; set; } = "";
    public bool MemoryLogging { get; set; } = true;
    public string SandboxRoot { get; set; } = "sandbox";
    public bool CompactMode { get; set; }

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cothon");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}