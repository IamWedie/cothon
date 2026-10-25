using System.Text.Json;

namespace Cothon.Core.Providers;

/// <summary>Turns provider-native JSON argument objects into arg dictionaries.</summary>
internal static class ToolArgs
{
    internal static IReadOnlyDictionary<string, object?> From(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                dict[property.Name] = Value(property.Value);
        return dict;
    }

    private static object? Value(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var whole) ? whole : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => From(e),
        JsonValueKind.Array => e.EnumerateArray().Select(Value).ToList(),
        _ => null,
    };
}
