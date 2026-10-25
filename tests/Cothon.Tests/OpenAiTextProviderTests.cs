using Cothon.Core.Providers;
using Cothon.Core.Screen;
using Cothon.Core.Sessions;
using Xunit;

public class OpenAiTextProviderTests
{
    private static HttpResponseMessage OkJson(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage OkChat(string text) => OkJson(
        $$"""{ "choices": [ { "message": { "role": "assistant", "content": "{{text}}" } } ] }""");

    [Fact]
    public async Task Parses_the_model_text_from_a_canned_response()
    {
        var provider = new OpenAiTextProvider("k", transport: stub(_ => OkChat("hi from openai")));

        var reply = await provider.CompleteAsync(new ModelRequest("SYS", [], "ping"));

        Assert.Equal("hi from openai", reply.Text);
        Assert.Empty(reply.ToolCalls);
    }

    [Fact]
    public async Task System_and_history_reach_the_payload()
    {
        HttpRequestMessage? captured = null;
        var provider = new OpenAiTextProvider("k", transport: stub(req => { captured = req; return OkChat("ok"); }));

        await provider.CompleteAsync(new ModelRequest(
            "SYS", [new ChatMessage("1", Author.Assistant, "earlier", DateTimeOffset.UtcNow)], "now"));

        var body = await BodyOf(captured);
        Assert.Contains("chat/completions", captured!.RequestUri!.ToString());
        Assert.Contains("\"system\"", body);
        Assert.Contains("SYS", body);
        Assert.Contains("\"assistant\"", body);
        Assert.Contains("earlier", body);
        Assert.Contains("\"user\"", body);
        Assert.Contains("now", body);
        Assert.DoesNotContain("Bearer", body); // auth lives in headers, not the body
        Assert.Equal("Bearer k", captured!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Screen_frame_travels_as_data_url()
    {
        HttpRequestMessage? captured = null;
        var provider = new OpenAiTextProvider("k", transport: stub(req => { captured = req; return OkChat("ok"); }));
        var frame = new ScreenFrame(Convert.FromBase64String("SGVsbG8="), 1280, 720, "image/jpeg");

        await provider.CompleteAsync(new ModelRequest("SYS", [], "look")
        {
            Screens = [frame],
        });

        var body = await BodyOf(captured);
        Assert.Contains("image_url", body);
        Assert.Contains("data:image/jpeg;base64,SGVsbG8=", body);
    }

    [Fact]
    public async Task Without_a_screen_no_image_parts()
    {
        HttpRequestMessage? captured = null;
        var provider = new OpenAiTextProvider("k", transport: stub(req => { captured = req; return OkChat("ok"); }));

        await provider.CompleteAsync(new ModelRequest("SYS", [], "text only"));

        var body = await BodyOf(captured);
        Assert.DoesNotContain("image_url", body);
    }

    [Fact]
    public async Task Missing_key_is_refused_before_any_http()
    {
        var provider = new OpenAiTextProvider("", transport: throwing());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new ModelRequest("S", [], "t")));
    }

    [Fact]
    public async Task Error_status_becomes_a_readable_exception()
    {
        var provider = new OpenAiTextProvider("bad", transport: stub(_ => new HttpResponseMessage(
            System.Net.HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"wrong key\"}}"),
        }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteAsync(new ModelRequest("S", [], "t")));
        Assert.Contains("401", ex.Message);
        Assert.Contains("wrong key", ex.Message);
    }

    private static async Task<string> BodyOf(HttpRequestMessage? request) =>
        await new StreamReader(request!.Content!.ReadAsStream()).ReadToEndAsync();

    [Fact]
    public async Task Tools_are_declared_as_functions_in_the_payload()
    {
        HttpRequestMessage? captured = null;
        var handler = stub(req => { captured = req; return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}", System.Text.Encoding.UTF8, "application/json"),
        }; });
        var provider = new OpenAiTextProvider("k", transport: handler);
        var request = new ModelRequest("You are Cothon.", [], "say hi")
        {
            Tools = [new ToolSpec("memory_save", "Store a durable fact.")],
        };

        await provider.CompleteAsync(request);

        var body = await BodyOf(captured);
        Assert.Contains("\"tools\"", body);
        Assert.Contains("memory_save", body);
        Assert.Contains("Store a durable fact.", body);
    }

    [Fact]
    public async Task Tool_call_reply_is_parsed_from_the_function_arguments_json()
    {
        var reply = $$"""
{
  "choices": [ { "message": {
    "content": null,
    "tool_calls": [ { "type": "function", "function": {
      "name": "memory_save",
      "arguments": "{\"key\":\"coffee\",\"value\":\"oat\",\"category\":\"personal\"}"
    } } ]
  } } ]
}
""";
        var handler = stub(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(reply, System.Text.Encoding.UTF8, "application/json"),
        });
        var provider = new OpenAiTextProvider("k", transport: handler);

        var result = await provider.CompleteAsync(new ModelRequest("S", [], "t"));

        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("memory_save", call.Name);
        Assert.Equal("coffee", call.Args["key"]);
        Assert.Equal("oat", call.Args["value"]);
        Assert.Equal("personal", call.Args["category"]);
    }

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
