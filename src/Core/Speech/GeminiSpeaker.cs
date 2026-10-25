using System.Net.Http.Json;
using System.Text.Json;
using Cothon.Core.Providers;

namespace Cothon.Core.Speech;

/// <summary>Raw PCM the speaker handed back, ready for a wave player.</summary>
public sealed record SpokenAudio(byte[] PcmBytes, int SampleRate, string MimeType);

/// <summary>
/// Speech synthesis through Gemini's audio-native TTS model. Transport is
/// injectable for tests; the model rarely refuses politely, so a null reply
/// is the contract for "no audio came back".
/// </summary>
public sealed class GeminiSpeaker(string apiKey, string voiceName = "Kore", HttpMessageHandler? transport = null)
{
    private const string Model = "gemini-2.5-flash-preview-tts";

    private readonly HttpClient _http = transport is null ? new() : new(transport);

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<SpokenAudio?> SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text } } },
            },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new
                {
                    voiceConfig = new { prebuiltVoiceConfig = new { voiceName } },
                },
            },
        };

        using var response = await _http.PostAsJsonAsync(
            $"{GeminiTextProvider.Endpoint(Model)}?key={apiKey}", body, PayloadOptions, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"Gemini TTS request failed ({(int)response.StatusCode}): {errorText}");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
        return Extract(payload);
    }

    private static SpokenAudio? Extract(JsonElement payload)
    {
        try
        {
            var inline = payload.GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("inlineData");
            var mimeType = inline.GetProperty("mimeType").GetString() ?? "";
            var rate = ParseRate(mimeType);
            if (rate == 0)
                return null;
            var data = inline.GetProperty("data").GetString() ?? "";
            return new SpokenAudio(Convert.FromBase64String(data), rate, mimeType.Split(';')[0]);
        }
        catch (KeyNotFoundException)
        {
            return null; // no inline audio in the reply
        }
    }

    internal static int ParseRate(string mimeType)
    {
        var match = System.Text.RegularExpressions.Regex.Match(mimeType, @"rate=(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }
}
