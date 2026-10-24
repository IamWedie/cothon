using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cothon.Core.Memory;

public sealed record MemoryFact(string Key, string Value, string Category, DateTimeOffset Updated);

/// <summary>
/// The single fact store: %LOCALAPPDATA%\Cothon\memory.json.
/// Secrets are redacted before anything touches disk. The file is written
/// atomically (temp + move) and a corrupt file is never silently extended.
/// </summary>
public sealed class MemoryStore
{
    private sealed record FactDto(string? Value, string? Category, DateTimeOffset Updated);

    private readonly string _path;
    private readonly bool _enabled;

    public MemoryStore(bool enabled, string? path = null)
    {
        _enabled = enabled;
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cothon", "memory.json");
    }

    public bool Enabled => _enabled;

    private Dictionary<string, Dictionary<string, FactDto>> ReadFacts()
    {
        if (!_enabled || !File.Exists(_path)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, FactDto>>>(File.ReadAllText(_path))
                   ?? [];
        }
        catch (JsonException)
        {
            // Refuse to write over a corrupt file: the data wins over convenience.
            throw new InvalidOperationException(
                $"Memory file at {_path} is corrupt; refusing to overwrite. Fix or delete it manually.");
        }
    }

    public void Remember(string key, string value, string category = "general")
    {
        if (!_enabled || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;

        var facts = ReadFacts();
        var bucket = facts.TryGetValue(category, out var b) ? b : new Dictionary<string, FactDto>();
        bucket[key] = new FactDto(Redact(value), category, DateTimeOffset.UtcNow);
        facts[category] = bucket;

        Write(facts);
    }

    /// <summary>All facts, newest first.</summary>
    public IReadOnlyList<MemoryFact> Recall()
    {
        if (!_enabled) return [];
        var unique = new Dictionary<string, MemoryFact>(StringComparer.Ordinal);
        foreach (var (category, bucket) in ReadFacts())
        {
            foreach (var (key, dto) in bucket)
            {
                if (dto.Value is null) continue;
                unique[key] = new MemoryFact(key, dto.Value, dto.Category ?? category, dto.Updated);
            }
        }
        return [.. unique.Values.OrderByDescending(f => f.Updated)];
    }

    public string? RecallText(string term)
    {
        var matches = Recall()
            .Where(f => f.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || f.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{f.Key}: {f.Value}")
            .ToList();
        return matches.Count == 0 ? null : string.Join("\n", matches);
    }

    public void Forget(string key)
    {
        if (!_enabled) return;
        var facts = ReadFacts();
        foreach (var bucket in facts.Values)
            bucket.Remove(key);
        foreach (var empty in facts.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
            facts.Remove(empty);
        Write(facts);
    }

    public void EraseAll()
    {
        if (!_enabled) return;
        Write([]);
    }

    private void Write(Dictionary<string, Dictionary<string, FactDto>> facts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }

    public static string Redact(string input) =>
        Regex.Replace(input, @"(?i)(api[_-]?key|token|secret|passwd|password)\s*[:=]\s*\S+", "$1=***REDACTED***");
}
