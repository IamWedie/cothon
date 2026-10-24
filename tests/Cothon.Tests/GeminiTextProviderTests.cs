using System.Net;
using System.Text.Json;
using Cothon.Core.Providers;
using Cothon.Core.Sessions;
using Xunit;

public class GeminiTextProviderTests
{
    private static ModelRequest Request() => new(
        "You are Cothon.", [], "say hi");

    private static HttpResponseMessage OkGemini(string text)
    {
        var json = $$"""
{
  "candidates": [ { "content": { "parts": [ { "text": "{{text}}" } ] } } ]
}
""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task Parses_the_model_text_from_a_canned_response()
    {
        var handler = new StubHandler(_ => OkGemini("hello from gemini"));
        var provider = new GeminiTextProvider("k", transport: handler);

        var reply = await provider.CompleteAsync(Request());

        Assert.Equal("hello from gemini", reply.Text);
        Assert.Empty(reply.ToolCalls);
    }

    [Fact]
    public async Task User_message_and_system_instruction_reach_the_payload()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req => { captured = req; return OkGemini("ok"); });
        var provider = new GeminiTextProvider("k", transport: handler);

        await provider.CompleteAsync(new ModelRequest("SYS", [new ChatMessage("1", Author.Assistant, "earlier", DateTimeOffset.UtcNow)], "now"), default);

        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.Contains("\"system_instruction\"", body);
        Assert.Contains("\"model\"", body);
        Assert.Contains("earlier", body);
        Assert.Contains("\"user\"", body);
        Assert.Contains("now", body);
        Assert.True(captured!.RequestUri!.ToString().Contains("gemini-2.5-flash"));
    }

    [Fact]
    public async Task Error_status_becomes_a_readable_exception()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"bad key\"}"),
        });
        var provider = new GeminiTextProvider("wrong", transport: handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteAsync(Request()));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Missing_key_is_refused_before_any_http()
    {
        var provider = new GeminiTextProvider("", transport: new ThrowingHandler());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(Request()));
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
