using System.Text.Json;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// The HTTP-GET render now seeds the diff-codec FRAME baseline (not just the HTML dedup
// string), so the FIRST interactive WS render diffs against the HTML the browser already
// holds instead of re-shipping the whole document. Previously the frame cache was seeded
// only by the first full-HTML interactive send, so the first state change after page load
// always shipped the body in full.
public class InitialRenderDiffTests
{
    // Auto is the framework default (the test host is created without a diffMode override); a
    // counter's text diff is far smaller than the body, so it takes the diff path under the
    // choose-smaller heuristic.

    [Fact]
    public async Task FirstInteraction_TextOnlyChange_ShipsDiffNotFullHtml()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // The VERY FIRST interaction: bump the counter (count=0 -> count=1, a pure text-node
        // change). With the GET-seeded frame baseline this ships a diff, not full HTML.
        await ws.SendJsonAsync(new { id = handlerId });
        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame!);
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("html", out _),
            $"First interaction must not ship full HTML. Got: {frame![..Math.Min(300, frame!.Length)]}");
        Assert.NotEmpty(doc.RootElement.GetProperty("ops").EnumerateArray());
    }
}
