using System.Net.Http.Json;
using System.Text.Json;
using Cothon.Core.Screen;
using Cothon.Core.Sessions;

namespace Cothon.Core.Providers;

/// <summary>
/// OpenAI text generation over the Chat Completions endpoint. Screens ride
/// as data-url images on the current user turn; the HTTP transport is
/// injectable so tests run without the network.
/// </summary>
public sealed class OpenAiTextProvider(string apiKey, string model = "gpt-4o-mini", HttpMessageHandler? transport = null) : IModelProvider
{
    public static string Endpoint(string model) =>
        $"https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _http = transport is null ? new() : new(transport);

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");

        var body = new
        {
            model,
            messages = request.History
                .Select(m => TextMessage(m.Author == Author.Assistant ? "assistant" : "user", [TextPart(m.Text)]))
                .Concat([TextMessage("user", CurrentParts(request.UserText, request.Screens))])
                .Prepend(TextMessage("system", [TextPart(request.SystemPrompt)])),
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, Endpoint(model));
        requestMessage.Headers.Authorization = new("Bearer", apiKey);
        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(body, PayloadOptions), System.Text.Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(requestMessage, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"OpenAI request failed ({(int)response.StatusCode}): {errorText}");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
        return new ModelReply(Normalise(payload), []);
    }

    internal static string Normalise(JsonElement payload)
    {
        try
        {
            return payload.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
        }
        catch (KeyNotFoundException)
        {
            return "(no text)";
        }
    }

    private static object TextPart(string text) => new { type = "text", text };

    private static object TextMessage(string role, object[] parts) => new { role, content = parts };

    private static object[] CurrentParts(string userText, IReadOnlyList<ScreenFrame>? screens)
    {
        var parts = new List<object> { TextPart(userText) };
        if (screens is { Count: > 0 })
        {
            foreach (var frame in screens)
            {
                var base64 = Convert.ToBase64String(frame.Bytes.ToArray());
                parts.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:{frame.MimeType};base64,{base64}" },
                });
            }
        }
        return [.. parts];
    }
}
