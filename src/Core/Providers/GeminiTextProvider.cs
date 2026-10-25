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

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = request.History
                .Select(m => new
                {
                    role = m.Author == Author.Assistant ? "model" : "user",
                    parts = TextOnly(m),
                })
                .Concat([CurrentUserTurn(request.UserText, request.Screens)]),
        };

        using var response = await _http.PostAsJsonAsync(
            $"{Endpoint(model)}?key={apiKey}", body, PayloadOptions, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"Gemini request failed ({(int)response.StatusCode}): {errorText}");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
        var text = Normalise(payload);
        return new ModelReply(text, []);
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
}
