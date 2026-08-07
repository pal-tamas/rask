using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     The development error overlay: in Development a fault the tree survived is reported <em>over</em>
///     the app instead of replacing it (#607).
/// </summary>
/// <remarks>
///     The behaviour under test is a whole-payload property — "the app is still there AND the error came
///     with it" — so these drive the real WebSocket round trip rather than a rendered component. That is
///     also the only place the two halves meet: the boundary decides, the payload carries.
/// </remarks>
[Collection("HostEnvironment")]
public sealed class DevErrorOverlayTests
{
    [Fact]
    public async Task In_development_a_handler_fault_keeps_the_app_and_reports_the_error_beside_it()
    {
        using var host = RaskTestHost.Create<ThrowingApp>(
            diffMode: LiveDiffMode.DisabledFull, environment: "Development");

        var payload = await FaultAsync(host);

        // The app is still on screen — this is the whole point. The full-document swap takes the scroll
        // position, the form input and the expanded panels with it, at the moment the developer most
        // wants to look at the state that produced the bug.
        Assert.Contains("count=", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", payload, StringComparison.Ordinal);

        // …and the error rides the same payload, rather than arriving as its own frame.
        Assert.Contains("\"devError\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"handler\"", payload, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", payload, StringComparison.Ordinal);
        Assert.Contains("boom", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_production_the_same_fault_still_replaces_the_page_and_carries_no_stack()
    {
        // Unchanged behaviour, deliberately: an end user gets the styled error page, and the payload must
        // not carry a stack trace to a browser that isn't a developer's.
        using var host = RaskTestHost.Create<ThrowingApp>(
            diffMode: LiveDiffMode.DisabledFull, environment: "Production");

        var payload = await FaultAsync(host);

        Assert.Contains("Something went wrong", payload, StringComparison.Ordinal);
        // The production error page names the exception type by design, so the claim here is narrower
        // and exact: no devError record, which is what would carry the stack trace.
        Assert.DoesNotContain("devError", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_render_fault_still_replaces_the_page_even_in_development()
    {
        // The one case the overlay must NOT take: the tree cannot be re-rendered as it stands, so keeping
        // the app on screen would mean re-running the render that just threw. Replacing the page is the
        // honest outcome, and it is what the issue's own note says.
        using var host = RaskTestHost.Create<ThrowingOnRenderApp>(
            diffMode: LiveDiffMode.DisabledFull, environment: "Development");

        var html = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();

        Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_overlay_is_shown_once_not_on_every_later_frame()
    {
        // The record is taken and cleared while the frame is built. Left in place it would ride every
        // subsequent payload, so an error you dismissed would reappear on the next click for the rest of
        // the session.
        using var host = RaskTestHost.Create<ThrowingApp>(
            diffMode: LiveDiffMode.DisabledFull, environment: "Development");

        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlers = Regex.Matches(initialHtml, "data-rask-on-click=\"(h\\d+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = handlers[0] });   // throw
        var faulted = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("\"devError\"", faulted!, StringComparison.Ordinal);

        await ws.SendJsonAsync(new { id = handlers[1] });   // bump — an ordinary click
        var next = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(next);
        Assert.DoesNotContain("devError", next, StringComparison.Ordinal);
    }

    // Connects, clicks the throwing button, and returns the payload that came back.
    private static async Task<string> FaultAsync(RaskTestHost host)
    {
        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var throwingId = Regex.Matches(initialHtml, "data-rask-on-click=\"(h\\d+)\"")
            .Select(m => m.Groups[1].Value)
            .First();

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = throwingId });
        var payload = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(payload);
        return payload!;
    }
}
