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
//
// Both transports. Over HTTP a frame that is not JSON spoils its whole POST (the body is one JSON array), so
// that request is refused 400 — and what must still hold is the same: the stream stays up, the session stays
// registered, and the next POST dispatches.
public class MalformedMessageTests
{
    // Assert against the full-HTML `html` field — force the legacy wire shape.

    private static readonly string[] _badFrames =
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

    public static TheoryData<string, LiveTransportKind> BadFrames()
    {
        var data = new TheoryData<string, LiveTransportKind>();
        foreach (var frame in _badFrames)
        {
            data.Add(frame, LiveTransportKind.WebSocket);
            data.Add(frame, LiveTransportKind.Http);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BadFrames))]
    public async Task BadFrame_IsDropped_SessionSurvivesAndKeepsDispatching(
        string badFrame, LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

        await using var ws = await LiveTestConnection.OpenAsync(host, transport, sessionId);
        Assert.Equal(1, host.Store.Count);

        await ws.SendRawAsync(badFrame);

        // A valid handler frame after the bad one must still dispatch and render.
        await ws.SendJsonAsync(new { id = handlerId });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.Contains("count=1", doc.RootElement.GetProperty("html").GetString()!);
        Assert.True(ws.IsOpen);
        Assert.Equal(1, host.Store.Count);
    }

    [Theory]
    [InlineData("type", LiveTransportKind.WebSocket)] // type as a non-string is treated as absent
    [InlineData("id", LiveTransportKind.WebSocket)]   // handler id as a non-string is treated as absent
    [InlineData("type", LiveTransportKind.Http)]
    [InlineData("id", LiveTransportKind.Http)]
    public async Task WrongFieldType_IsIgnored_NoTeardown(string field, LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var sessionId = MarkupAssert.SessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

        await using var ws = await LiveTestConnection.OpenAsync(host, transport, sessionId);

        // Field present but the wrong JSON type (a number where a string is expected).
        await ws.SendRawAsync($"{{\"{field}\":123}}");

        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.Null(text);
        Assert.True(ws.IsOpen);
        Assert.Equal(1, host.Store.Count);
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task ManyBadFramesInARow_DoNotDropTheSession(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

        await using var ws = await LiveTestConnection.OpenAsync(host, transport, sessionId);

        for (var i = 0; i < 25; i++)
        {
            await ws.SendRawAsync("{garbage" + i);
        }

        await ws.SendJsonAsync(new { id = handlerId });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        Assert.True(ws.IsOpen);
        Assert.Equal(1, host.Store.Count);
    }
}
