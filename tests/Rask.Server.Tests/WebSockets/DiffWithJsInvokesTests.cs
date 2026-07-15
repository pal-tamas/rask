using System.Text.Json;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Regression guard for the Server "typing re-renders the whole content" bug. A
// component that invokes IJSRuntime on every render used to force the full-HTML path
// on every keystroke because the diff gate skipped whenever jsInvokes were pending.
// The diff payload now carries fire-and-forget invokes, so a bound-input keystroke
// ships a small kind:"diff" frame (with the invoke attached) — matching WASM, which
// never had the gate.
public class DiffWithJsInvokesTests
{
    [Fact]
    public async Task TypingWithPerRenderJsInvoke_ShipsDiffCarryingTheInvoke()
    {
        using var host = RaskTestHost.Create<JsInvokeBindingApp>();
        var initial = await host.Http.GetAsync("/");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var inputId = MarkupAssert.RequireAttr(initialHtml, "data-rask-on-input");

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        // Drain the hello-time catch-up frame (queued first-render invoke).
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // Keystroke 1: mutate the bound value. The echo <p> text changes, so the
        // render differs and a frame is sent.
        await ws.SendJsonAsync(new { id = inputId, value = "ab" });
        var frame1 = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        AssertDiffCarryingInvoke(frame1, "first keystroke");

        // Keystroke 2: proves _forceFullHtmlNextRender no longer sticks after a
        // diff+jsInvokes frame — a second keystroke also ships a diff.
        await ws.SendJsonAsync(new { id = inputId, value = "abc" });
        var frame2 = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        AssertDiffCarryingInvoke(frame2, "second keystroke");
    }

    private static void AssertDiffCarryingInvoke(string? frame, string label)
    {
        Assert.False(string.IsNullOrEmpty(frame), $"no frame received for {label}");
        using var doc = JsonDocument.Parse(frame!);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("kind", out var kind) && kind.GetString() == "diff",
            $"expected kind:diff for {label} but got: {frame}");

        Assert.True(root.TryGetProperty("jsInvokes", out var invokes) && invokes.GetArrayLength() > 0,
            $"expected jsInvokes on the diff frame for {label} but got: {frame}");
        Assert.Equal("test.noop", invokes[0].GetProperty("identifier").GetString());

        // The change is an in-place text edit, so an UpdateText op must be present and
        // there must be NO full "html" field.
        Assert.False(root.TryGetProperty("html", out _), $"diff frame for {label} must not carry full html");
        Assert.True(root.GetProperty("ops").GetArrayLength() > 0, $"diff frame for {label} had no ops");
    }
}
