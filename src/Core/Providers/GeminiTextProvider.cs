using System.Net.Http.Json;
using System.Text.Json;
using Cothon.Core.Screen;
using Cothon.Core.Sessions;

namespace Cothon.Core.Providers;

/// <summary>
/// Gemini text generation over the REST generateContent endpoint. The HTTP
/// transport is injectable so tests can replay canned responses without the
/// network; nothing here may depend on the UI.
/// </summary>
public sealed class GeminiTextProvider(string apiKey, string model = "gemini-2.5-flash", HttpMessageHandler? transport = null) : IModelProvider
{
    public static string Endpoint(string model) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

    private readonly HttpClient _http = transport is null ? new() : new(transport);

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        var contents = request.History
            .Select(m => new
            {
                role = m.Author == Author.Assistant ? "model" : "user",
                parts = TextOnly(m),
            })
            .Concat(TurnSteps(request.ToolSteps))
            .Concat([CurrentUserTurn(request.UserText, request.Screens)]);
        var system = new { parts = new[] { new { text = request.SystemPrompt } } };

        object body = request.Tools is { Count: > 0 }
            ? new
            {
                system_instruction = system,
                contents,
                tools = new[]
                {
                    new { functionDeclarations = request.Tools.Select(t => new { name = t.Name, description = t.Description }) },
                },
            }
            : new { system_instruction = system, contents };

        using var response = await _http.PostAsJsonAsync(
            $"{Endpoint(model)}?key={apiKey}", body, PayloadOptions, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"Gemini request failed ({(int)response.StatusCode}): {errorText}");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
        return new ModelReply(Normalise(payload), ToolCalls(payload));
    }

    private static object TextOnly(ChatMessage m) => new
    {
        role = m.Author == Author.Assistant ? "model" : "user",
        parts = new object[] { new { text = m.Text } },
    };

    /// <summary>The current turn speaks text, plus any screens read this round.</summary>
    private static object CurrentUserTurn(string userText, IReadOnlyList<ScreenFrame>? screens)
    {
        var parts = new List<object> { new { text = userText } };

        if (screens is { Count: > 0 })
        {
            foreach (var frame in screens)
            {
                parts.Add(new
                {
                    inline_data = new
                    {
                        mime_type = frame.MimeType,
                        data = Convert.ToBase64String(frame.Bytes.ToArray()),
                    },
                });
            }
        }

        return new { role = "user", parts = parts.ToArray() };
    }

    /// <summary>Each round replays as a model call turn followed by its function responses.</summary>
    private static IEnumerable<object> TurnSteps(IReadOnlyList<ModelTurnStep>? steps)
    {
        if (steps is null)
            yield break;

        foreach (var step in steps)
        {
            var modelParts = new List<object>();
            if (step.AssistantText.Length > 0)
                modelParts.Add(new { text = step.AssistantText });
            foreach (var executed in step.Calls)
                modelParts.Add(new
                {
                    functionCall = new
                    {
                        name = executed.Call.Name,
                        args = ArgsToPlain(executed.Call.Args),
                    },
                });
            if (modelParts.Count > 0)
                yield return new { role = "model", parts = modelParts.ToArray() };

            var responses = step.Calls.Select(executed => (object)new
            {
                functionResponse = new
                {
                    name = executed.Call.Name,
                    response = new
                    {
                        result = executed.Ok ? executed.Outcome : $"Error: {executed.Outcome}",
                    },
                },
            });
            yield return new { role = "user", parts = responses.ToArray() };
        }
    }

    /// <summary>Args were already flattened to simple types when parsed.</summary>
    private static Dictionary<string, object?> ArgsToPlain(IReadOnlyDictionary<string, object?> args) =>
        new(args);

    /// <summary>Extracts first candidate → first part text defensively.</summary>
    internal static string Normalise(JsonElement payload)
    {
        try
        {
            return payload.GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";
        }
        catch (KeyNotFoundException)
        {
            return "(no text)";
        }
    }

    /// <summary>Collects functionCall parts into ModelToolCalls; text-only replies get none.</summary>
    internal static IReadOnlyList<ModelToolCall> ToolCalls(JsonElement payload)
    {
        try
        {
            return payload.GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")
                .EnumerateArray()
                .Where(part => part.TryGetProperty("functionCall", out _))
                .Select(part => part.GetProperty("functionCall"))
                .Select(fc => new ModelToolCall(
                    fc.GetProperty("name").GetString() ?? "",
                    ToolArgs.From(fc.GetProperty("args"))))
                .ToList();
        }
        catch (KeyNotFoundException)
        {
            return [];
        }
    }
}
