using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cothon.Core.Providers;
using Cothon.Core.Sessions;

namespace Cothon.Core.Audio;

/// <summary>
/// Speech-to-text via Gemini generateContent: the WAV rides inline and the
/// model's next text is treated as the transcript. Transport injectable for
/// tests; network errors never crash a voice turn.
/// </summary>
public sealed class GeminiTranscriber(string apiKey, string model = "gemini-2.5-flash", HttpMessageHandler? transport = null)
{
    private readonly HttpClient _http = transport is null ? new() : new(transport);

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<string?> TranscribeAsync(VoiceClip clip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        var body = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = clip.MimeType, data = Convert.ToBase64String(clip.WavBytes) } },
                        new { text = "Transcribe exactly what is said in this audio. Reply with the spoken words only; never comment." },
                    },
                },
            },
        };

        using var response = await _http.PostAsJsonAsync(
            $"{GeminiTextProvider.Endpoint(model)}?key={apiKey}", body, PayloadOptions, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"Gemini transcription failed ({(int)response.StatusCode}): {errorText}");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
        var text = GeminiTextProvider.Normalise(payload).Trim();
        return text.Length == 0 ? null : text;
    }
}
