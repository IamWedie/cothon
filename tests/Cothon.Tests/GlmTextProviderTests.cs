using Cothon.Core.Providers;
using Cothon.Core.Sessions;
using Xunit;

public class GlmTextProviderTests
{
    private static HttpResponseMessage OkChat(string text) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{ "choices": [ { "message": { "role": "assistant", "content": "{{text}}" } } ] }""",
            System.Text.Encoding.UTF8, "application/json"),
    };

    private static HttpMessageHandler stub(Func<HttpRequestMessage, HttpResponseMessage> reply) =>
        new StubHandler(reply);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(reply(request));
    }

    [Fact]
    public async Task Speaks_the_openai_wire_over_the_zai_endpoint()
    {
        HttpRequestMessage? captured = null;
        var provider = new GlmTextProvider("z-key", transport: stub(req => { captured = req; return OkChat("ok"); }));

        var reply = await provider.CompleteAsync(new ModelRequest("S", [], "hi"));

        Assert.Equal("ok", reply.Text);
        Assert.Contains("api.z.ai/api/paas/v4/chat/completions", captured!.RequestUri!.ToString());
        Assert.DoesNotContain("api.openai.com", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer z-key", captured!.Headers.Authorization!.ToString());
        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.Contains("\"model\":\"glm-5.3-flash\"", body.Replace(" ", "").Replace("\n", "").Replace("\r", ""));
    }

    [Fact]
    public async Task Tools_declared_and_tool_calls_parsed_same_as_openai()
    {
        HttpRequestMessage? captured = null;
        var reply = """
        { "choices": [ { "message": { "content": null, "tool_calls": [ { "id": "call_9", "type": "function", "function": { "name": "memory_save", "arguments": "{\"key\":\"a\",\"value\":\"b\"}" } } ] } } ] }
        """;
        var provider = new GlmTextProvider("z-key", transport: stub(req =>
        {
            captured = req;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(reply, System.Text.Encoding.UTF8, "application/json"),
            };
        }));

        var result = await provider.CompleteAsync(new ModelRequest("S", [], "t")
        {
            Tools = [new ToolSpec("memory_save", "Store a durable fact.")],
        });

        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.Contains("memory_save", body);
        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("memory_save", call.Name);
        Assert.Equal("b", call.Args["value"]);
    }

    [Fact]
    public async Task Missing_key_is_refused_before_any_http()
    {
        var provider = new GlmTextProvider("");
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new ModelRequest("S", [], "t")));
    }

    [Fact]
    public async Task Error_status_speaks_glm()
    {
        var provider = new GlmTextProvider("bad", transport: stub(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"wrong key\"}}"),
        }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteAsync(new ModelRequest("S", [], "t")));
        Assert.Contains("GLM", ex.Message);
    }
}
