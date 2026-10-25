using Cothon.Core.Audio;
using Cothon.Core.Sessions;
using Xunit;

public class GeminiTranscriberTests
{
    private static HttpResponseMessage OkJson(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage ReplyText(string text)
    {
        var json = $$"""
{
  "candidates": [ { "content": { "parts": [ { "text": "{{text}}" } ] } } ]
}
""";
        return OkJson(json);
    }

    [Fact]
    public async Task Sends_the_clip_as_inline_wav_and_returns_the_transcript()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req => { captured = req; return ReplyText("hello there"); });
        var transcriber = new GeminiTranscriber("k", transport: handler);
        var clip = VoiceClip.FromPcm(new byte[] { 1, 2, 3, 4 }, minimumSeconds: 0)!;

        var transcript = await transcriber.TranscribeAsync(clip);

        Assert.Equal("hello there", transcript);
        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.Contains("audio/wav", body);
        Assert.Contains(Base64Of(clip.WavBytes), body);
    }

    [Fact]
    public async Task Silence_and_empty_texts_are_reported_as_nothing_heard()
    {
        var transcriber = new GeminiTranscriber("k", transport: stub(_ => ReplyText("")));

        Assert.Null(await transcriber.TranscribeAsync(ShortClip()));
    }

    [Fact]
    public async Task Missing_key_is_refused_before_any_http()
    {
        var transcriber = new GeminiTranscriber("", transport: throwing());
        await Assert.ThrowsAsync<InvalidOperationException>(() => transcriber.TranscribeAsync(ShortClip()));
    }

    [Fact]
    public async Task Error_status_becomes_a_readable_exception()
    {
        var transcriber = new GeminiTranscriber("bad", transport: stub(_ => new HttpResponseMessage(
            System.Net.HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"bad key\"}"),
        }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => transcriber.TranscribeAsync(ShortClip()));
        Assert.Contains("401", ex.Message);
    }

    private static VoiceClip ShortClip() => VoiceClip.FromPcm([1, 2, 3, 4], minimumSeconds: 0)!;

    private static string Base64Of(byte[] bytes) => Convert.ToBase64String(bytes);

    private static HttpMessageHandler stub(Func<HttpRequestMessage, HttpResponseMessage> reply) =>
        new StubHandler(reply);

    private static HttpMessageHandler throwing() => new ThrowingHandler();

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
