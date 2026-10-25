using Cothon.Core.Providers;
using Cothon.Core.Screen;
using Cothon.Core.Sessions;
using Xunit;

public class AnthropicTextProviderTests
{
    private static HttpResponseMessage OkJson(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage OkClaude(string text) => OkJson(
        $$"""{ "content": [ { "type": "text", "text": "{{text}}" } ] }""");

    [Fact]
    public async Task Parses_the_model_text_from_a_canned_response()
    {
        var provider = new AnthropicTextProvider("k", transport: stub(_ => OkClaude("hi from claude")));

        var reply = await provider.CompleteAsync(new ModelRequest("SYS", [], "ping"));

        Assert.Equal("hi from claude", reply.Text);
        Assert.Empty(reply.ToolCalls);
    }

    [Fact]
    public async Task System_prompt_and_history_reach_the_payload()
    {
        HttpRequestMessage? captured = null;
        var provider = new AnthropicTextProvider("k", transport: stub(req => { captured = req; return OkClaude("ok"); }));

        await provider.CompleteAsync(new ModelRequest(
            "SYS", [new ChatMessage("1", Author.Assistant, "earlier", DateTimeOffset.UtcNow)], "now"));

        var body = await BodyOf(captured);
        Assert.Contains("v1/messages", captured!.RequestUri!.ToString());
        Assert.Contains("\"system\"", body);
        Assert.Contains("SYS", body);
        Assert.Contains("\"assistant\"", body);
        Assert.Contains("earlier", body);
        Assert.Contains("\"user\"", body);
        Assert.Contains("now", body);
        Assert.Equal("2023-06-01", captured!.Headers.TryGetValues("anthropic-version", out var v) ? v!.First() : "");
        Assert.Equal("k", captured!.Headers.TryGetValues("x-api-key", out var k) ? k!.First() : "");
    }

    [Fact]
    public async Task Screen_frame_travels_as_base64_source()
    {
        HttpRequestMessage? captured = null;
        var provider = new AnthropicTextProvider("k", transport: stub(req => { captured = req; return OkClaude("ok"); }));
        var frame = new ScreenFrame(Convert.FromBase64String("SGVsbG8="), 1280, 720, "image/jpeg");

        await provider.CompleteAsync(new ModelRequest("SYS", [], "look")
        {
            Screens = [frame],
        });

        var body = await BodyOf(captured);
        Assert.Contains("\"image\"", body);
        Assert.Contains("base64", body);
        Assert.Contains("SGVsbG8=", body);
        Assert.Contains("image/jpeg", body);
    }

    [Fact]
    public async Task Without_a_screen_no_image_blocks()
    {
        HttpRequestMessage? captured = null;
        var provider = new AnthropicTextProvider("k", transport: stub(req => { captured = req; return OkClaude("ok"); }));

        await provider.CompleteAsync(new ModelRequest("SYS", [], "text only"));

        var body = await BodyOf(captured);
        Assert.DoesNotContain("\"image\"", body);
    }

    [Fact]
    public async Task Missing_key_is_refused_before_any_http()
    {
        var provider = new AnthropicTextProvider("", transport: throwing());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new ModelRequest("S", [], "t")));
    }

    [Fact]
    public async Task Error_status_becomes_a_readable_exception()
    {
        var provider = new AnthropicTextProvider("bad", transport: stub(_ => new HttpResponseMessage(
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

    [Fact]
    public async Task Tools_are_declared_in_the_payload()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req => { captured = req; return OkJson("{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}"); });
        var provider = new AnthropicTextProvider("k", transport: handler);
        var request = new ModelRequest("You are Cothon.", [], "say hi")
        {
            Tools = [new ToolSpec("memory_save", "Store a durable fact.")],
        };

        await provider.CompleteAsync(request);

        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.Contains("\"tools\"", body);
        Assert.Contains("memory_save", body);
        Assert.Contains("Store a durable fact.", body);
    }

    [Fact]
    public async Task Tool_use_reply_is_parsed_into_a_tool_call()
    {
        var json = """
{
  "content": [
    { "type": "tool_use", "id": "tu_1", "name": "memory_save", "input": { "key": "coffee", "value": "oat", "category": "personal" } }
  ]
}
""";
        var handler = new StubHandler(_ => OkJson(json));
        var provider = new AnthropicTextProvider("k", transport: handler);

        var reply = await provider.CompleteAsync(new ModelRequest("S", [], "t"));

        var call = Assert.Single(reply.ToolCalls);
        Assert.Equal("memory_save", call.Name);
        Assert.Equal("tu_1", call.Id);
        Assert.Equal("coffee", call.Args["key"]);
        Assert.Equal("oat", call.Args["value"]);
        Assert.Equal("personal", call.Args["category"]);
    }

    [Fact]
    public async Task Tool_results_come_back_as_tool_result_blocks()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req => { captured = req; return OkJson("{\"content\":[{\"type\":\"text\",\"text\":\"got it\"}]}"); });
        var provider = new AnthropicTextProvider("k", transport: handler);
        var request = new ModelRequest("S", [], "done?")
        {
            ToolSteps =
            [
                new ModelTurnStep(
                    AssistantText: "saving that",
                    Calls: [new ModelCallExecuted(
                        new ModelToolCall("memory_save", new Dictionary<string, object?> { ["key"] = "coffee" }) { Id = "tu_9" },
                        Ok: true,
                        Outcome: "Remembered coffee.")]),
            ],
        };

        await provider.CompleteAsync(request);

        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.Contains("\"type\":\"tool_use\"", body);
        Assert.Contains("\"type\":\"tool_result\"", body);
        Assert.Contains("\"tool_use_id\":\"tu_9\"", body);
        Assert.Contains("Remembered coffee.", body);
    }
}

