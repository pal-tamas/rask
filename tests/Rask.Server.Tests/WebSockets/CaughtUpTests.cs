using System.Net.WebSockets;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A `.Confirm` yes presses its button again once the browser holds every page the server sent before it
// (rask-confirm.ts): the browser asks `{type:"caughtUp",n}` and the server answers the same frame behind every
// page already sent and every event queued before it.
public class CaughtUpTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void The_answer_is_written_as_the_json_the_client_parses(long asked)
    {
        Span<byte> buffer = stackalloc byte[64];

        var written = RaskEndpointExtensions.WriteCaughtUp(buffer, asked);

        Assert.Equal($$"""{"type":"caughtUp","n":{{asked}}}""", System.Text.Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public async Task The_answer_comes_after_the_page_of_an_event_sent_before_the_question()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var (ws, handlerId) = await ConnectAsync(host);

        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "caughtUp", n = 3 }, ct: TestContext.Current.CancellationToken);
        var page = await ws.ReceiveTextAsync();
        var answer = await ws.ReceiveTextAsync();

        Assert.Contains("count=1", page);
        Assert.Equal("""{"type":"caughtUp","n":3}""", answer);
    }

    [Fact]
    public async Task A_question_without_a_number_is_not_answered()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var (ws, _) = await ConnectAsync(host);

        await ws.SendJsonAsync(new { type = "caughtUp", n = "three" }, ct: TestContext.Current.CancellationToken);
        var frames = await ws.SettledAsync();

        Assert.DoesNotContain(frames, frame => frame.Contains("caughtUp", StringComparison.Ordinal));
    }

    private static async Task<(WebSocket ws, string handlerId)> ConnectAsync(RaskTestHost host)
    {
        var initial = await host.Http.GetAsync("/start");
        var html = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(html);
        var handlerId = MarkupAssert.FirstHandlerId(html);

        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.SettledAsync();
        return (ws, handlerId);
    }
}
