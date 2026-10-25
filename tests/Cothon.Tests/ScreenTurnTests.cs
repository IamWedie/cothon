using Cothon.Core.Providers;
using Cothon.Core.Sessions;
using Cothon.Core.Policy;
using Cothon.Core.Tools;
using Cothon.Core.Undo;
using Cothon.Core.Screen;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// Screen reading: the frame travels intact from the capture seam through
/// the loop into the provider payload — and nothing leaks when disabled.
/// </summary>
public sealed class ScreenTurnTests
{
    [Fact]
    public async Task Loop_passes_the_captured_frame_to_the_provider()
    {
        ScreenRequestRecorder recorder = new();
        var loop = new AgentLoop(recorder, DefaultBroker());
        var frame = new ScreenFrame([1, 2, 3, 4], 800, 600, "image/jpeg");

        await loop.RunAsync([], "look at this", screen: frame);

        var sent = Assert.Single(recorder.Requests);
        var sentFrame = Assert.Single(sent.Screens!);
        Assert.Equal(800, sentFrame.Width);
        Assert.Equal(600, sentFrame.Height);
        Assert.Equal("image/jpeg", sentFrame.MimeType);
    }

    [Fact]
    public async Task No_screen_when_capture_disabled_or_empty()
    {
        ScreenRequestRecorder recorder = new();
        var loop = new AgentLoop(recorder, DefaultBroker());

        await loop.RunAsync([], "just text", screen: null);

        var sent = Assert.Single(recorder.Requests);
        Assert.Empty(sent.Screens!);
    }

    [Fact]
    public async Task Screen_frame_reaches_the_gemini_payload_as_inline_data()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req => { captured = req; return OkGemini("read it"); });
        var provider = new GeminiTextProvider("k", transport: handler);
        var frame = new ScreenFrame(
            Convert.FromBase64String("SGVsbG8="), 1280, 720, "image/jpeg");

        await provider.CompleteAsync(new ModelRequest("SYS", [], "what's on screen?")
        {
            Screens = [frame],
        });

        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.Contains("inline_data", body);
        Assert.Contains("image/jpeg", body);
        Assert.Contains("SGVsbG8=", body);
        Assert.Contains("what's on screen?", body);
    }

    [Fact]
    public async Task Without_a_screen_no_inline_data_is_sent()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req => { captured = req; return OkGemini("ok"); });
        var provider = new GeminiTextProvider("k", transport: handler);

        await provider.CompleteAsync(new ModelRequest("SYS", [], "text only"));

        var body = await new StreamReader(captured!.Content!.ReadAsStream()).ReadToEndAsync();
        Assert.DoesNotContain("inline_data", body);
    }

    private static ToolBroker DefaultBroker() => new(
        new ToolRegistry(), new PolicyEngine(), new AutoApprovalGate(), new UndoLedger());

    private sealed class ScreenRequestRecorder : IModelProvider
    {
        public List<ModelRequest> Requests { get; } = [];

        public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            await Task.Yield();
            return new ModelReply("done", []);
        }
    }

    private static HttpResponseMessage OkGemini(string text)
    {
        var json = $$"""
{
  "candidates": [ { "content": { "parts": [ { "text": "{{text}}" } ] } } ]
}
""";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(reply(request));
    }
}
