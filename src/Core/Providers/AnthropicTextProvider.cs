using System.Net.Http.Json;
using System.Text.Json;
using Cothon.Core.Screen;
using Cothon.Core.Sessions;

namespace Cothon.Core.Providers;

/// <summary>
/// Anthropic Claude over the Messages endpoint. Tool/system events map into
/// the nearest user turn as plain text; screens ride as base64 image blocks
/// on the current user turn. Transport is injectable for tests.
/// </summary>
public sealed class AnthropicTextProvider(string apiKey, string model = "claude-sonnet-4-5", HttpMessageHandler? transport = null) : IModelProvider
{
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _http = transport is null ? new() : new(transport);

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Anthropic API key is not configured.");

        var messages = request.History
            .Select(m => new
            {
                role = m.Author == Author.Assistant ? "assistant" : "user",
                content = TextNote(m),
            })
            .Concat([new
            {
                role = "user",
                content = CurrentContent(request.UserText, request.Screens),
            }]);

        object body = request.Tools is { Count: > 0 }
            ? new
            {
                model,
                max_tokens = 2048,
                system = request.SystemPrompt,
                messages,
                tools = request.Tools.Select(t => new { name = t.Name, description = t.Description }),
            }
            : new { model, max_tokens = 2048, system = request.SystemPrompt, messages };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        requestMessage.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        requestMessage.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(body, PayloadOptions), System.Text.Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(requestMessage, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"Anthropic request failed ({(int)response.StatusCode}): {errorText}");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
        return new ModelReply(Normalise(payload), ToolCalls(payload));
    }

    public static string Endpoint => "https://api.anthropic.com/v1/messages";

    internal static string Normalise(JsonElement payload)
    {
        try
        {
            foreach (var block in payload.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text")
                    return block.GetProperty("text").GetString() ?? "";
            }
            return "(no text)";
        }
        catch (KeyNotFoundException)
        {
            return "(no text)";
        }
    }

    /// <summary>History turns speak text; tool/system lines ride as user notes.</summary>
    private static object[] TextNote(ChatMessage m) => [new { type = "text", text = m.Text }];

    /// <summary>tool_use blocks carry input args as a plain object.</summary>
    internal static IReadOnlyList<ModelToolCall> ToolCalls(JsonElement payload)
    {
        try
        {
            return payload.GetProperty("content")
                .EnumerateArray()
                .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "tool_use")
                .Select(block => new ModelToolCall(
                    block.GetProperty("name").GetString() ?? "",
                    ToolArgs.From(block.GetProperty("input"))))
                .ToList();
        }
        catch (KeyNotFoundException)
        {
            return [];
        }
    }

    private static object[] CurrentContent(string userText, IReadOnlyList<ScreenFrame>? screens)
    {
        var content = new List<object> { new { type = "text", text = userText } };

        if (screens is { Count: > 0 })
        {
            foreach (var frame in screens)
            {
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = frame.MimeType,
                        data = Convert.ToBase64String(frame.Bytes.ToArray()),
                    },
                });
            }
        }
        return [.. content];
    }
}
