using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A live session must survive any single bad inbound frame. The receive loop's only catches
// are OperationCanceledException / WebSocketException, so before the guard an unguarded
// JsonDocument.Parse (or a TryGetProperty against a non-object root) threw straight out of the
// loop, detached the socket and scheduled the session for removal — one buggy or adversarial
// frame dropped the whole session. These tests assert each bad frame is dropped and the loop
// keeps dispatching.
public class MalformedMessageTests
{
    // Assert against the full-HTML `html` field — force the legacy wire shape.

    public static TheoryData<string> BadFrames() =>
    [
        "{not-json",                        // not JSON at all
        "{\"type\":",                       // truncated object
        "[1,2,3]",                          // valid JSON, but an array root (TryGetProperty would throw)
        "5",                                // valid JSON number root
        "\"hello\"",                        // valid JSON string root
        "true",                             // valid JSON bool root
        "   ",                              // whitespace-only (parse throws)
        "{\"type\":\"hello\",\"session\":"  // truncated mid-value
    ];

    [Theory]
    [MemberData(nameof(BadFrames))]
    public async Task BadFrame_IsDropped_SessionSurvivesAndKeepsDispatching(string badFrame)
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = Markup.SessionId(initialHtml);
        var handlerId = Markup.FirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, host.Store.Count);

        await ws.SendAsync(Encoding.UTF8.GetBytes(badFrame), WebSocketMessageType.Text, true, CancellationToken.None);

        // A valid handler frame after the bad one must still dispatch and render.
        await ws.SendJsonAsync(new { id = handlerId });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.Contains("count=1", doc.RootElement.GetProperty("html").GetString()!);
        Assert.Equal(WebSocketState.Open, ws.State);
        Assert.Equal(1, host.Store.Count);
    }

    [Theory]
    [InlineData("type")] // type as a non-string is treated as absent
    [InlineData("id")]   // handler id as a non-string is treated as absent
    public async Task WrongFieldType_IsIgnored_NoTeardown(string field)
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var sessionId = Markup.SessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // Field present but the wrong JSON type (a number where a string is expected).
        var json = $"{{\"{field}\":123}}";
        await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.Null(text);
        Assert.Equal(WebSocketState.Open, ws.State);
        Assert.Equal(1, host.Store.Count);
    }

    [Fact]
    public async Task ManyBadFramesInARow_DoNotDropTheSession()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = Markup.SessionId(initialHtml);
        var handlerId = Markup.FirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 25; i++)
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes("{garbage" + i), WebSocketMessageType.Text, true,
                CancellationToken.None);
        }

        await ws.SendJsonAsync(new { id = handlerId });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        Assert.Equal(WebSocketState.Open, ws.State);
        Assert.Equal(1, host.Store.Count);
    }
}
