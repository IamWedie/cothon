using Cothon.Core.Speech;
using Cothon.Core.Sessions;
using Xunit;

public class GeminiSpeakerTests
{
    private static HttpResponseMessage OkJson(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage CorrectAnswer(string mimeType, string base64) => OkJson($$"""
{
  "candidates": [ { "content": { "parts": [ {
    "inlineData": { "mimeType": "{{mimeType}}", "data": "{{base64}}" }
  } ] } } ]
}
""");

    private static HttpMessageHandler stub(Func<HttpRequestMessage, HttpResponseMessage> reply) =>
        new StubHandler(reply);

    private static HttpMessageHandler throwing() => new ThrowingHandler();

    [Fact]
    public async Task Asks_for_audio_with_a_named_voice()
    {
        HttpRequestMessage? captured = null;
        var handler = stub(req => { captured = req; return CorrectAnswer("audio/L16;rate=24000", Convert.ToBase64String([1, 2, 3, 4])); });
        var speaker = new GeminiSpeaker("k", voiceName: "Kore", transport: handler);

        var audio = await speaker.SpeakAsync("hello");

        Assert.NotNull(audio);
        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.Contains("\"responseModalities\":[\"AUDIO\"]", body);
        Assert.Contains("\"voiceName\":\"Kore\"", body);
        Assert.Contains("hello", body);
    }

    [Fact]
    public async Task Returned_pcm_carries_the_sample_rate_from_the_mime_type()
    {
        var handler = stub(_ => CorrectAnswer("audio/L16;rate=24000;bits=16", Convert.ToBase64String(new byte[] { 9, 8, 7, 6 })));
        var speaker = new GeminiSpeaker("k", transport: handler);

        var audio = await speaker.SpeakAsync("hi");

        Assert.Equal(24000, audio!.SampleRate);
        Assert.Equal("audio/L16", audio.MimeType);
        Assert.Equal([9, 8, 7, 6], audio.PcmBytes);
    }

    [Fact]
    public async Task Answer_without_inline_audio_is_reported_null()
    {
        var handler = stub(_ => OkJson("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"i cannot speak\"}]}}]}"));
        var speaker = new GeminiSpeaker("k", transport: handler);

        Assert.Null(await speaker.SpeakAsync("hi"));
    }

    [Fact]
    public async Task Missing_key_is_refused_before_any_http()
    {
        var speaker = new GeminiSpeaker("", transport: throwing());
        await Assert.ThrowsAsync<InvalidOperationException>(() => speaker.SpeakAsync("hi"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(reply(request));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("no HTTP expected");
    }
}
