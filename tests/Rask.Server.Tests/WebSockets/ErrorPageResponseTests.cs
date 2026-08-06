using System.Net;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     What the network sees when a page crashes, and what the user can do about it (#607).
/// </summary>
public sealed class ErrorPageResponseTests
{
    [Fact]
    public async Task A_page_that_throws_while_rendering_answers_500_not_200()
    {
        // The root boundary catches the exception and renders the error document, so the response is
        // perfectly ordinary HTML and nothing downstream could tell it apart from a healthy page: caches
        // would store it, crawlers would index it, and an uptime check would report the site green.
        using var host = RaskTestHost.Create<ThrowingOnRenderApp>(diffMode: LiveDiffMode.DisabledFull);

        var response = await host.Http.GetAsync("/start");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        // The body is unchanged: still the error page, not an empty 500.
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_renders_fine_still_answers_200()
    {
        using var host = RaskTestHost.Create<ThrowingApp>(diffMode: LiveDiffMode.DisabledFull);

        var response = await host.Http.GetAsync("/start");

        // ThrowingApp only throws from a click handler, so its initial render is healthy.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_error_page_offers_an_in_session_retry_alongside_the_reload()
    {
        // RootErrorBoundary used to discard the boundary's Recover — `(ex, _) =>` — leaving a full page
        // reload as the only way out of a fault that had not damaged anything.
        using var host = RaskTestHost.Create<ThrowingOnRenderApp>(diffMode: LiveDiffMode.DisabledFull);

        var html = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();

        Assert.Contains("Try again", html, StringComparison.Ordinal);
        Assert.Contains("Reload this page", html, StringComparison.Ordinal);
        // Try again is a live handler, not a link or a reload in disguise.
        Assert.Matches(new Regex("data-rask-on-click=\"h\\d+\"[^>]*>Try again", RegexOptions.None), html);
    }

    [Fact]
    public async Task Try_again_clears_the_error_and_puts_the_app_back()
    {
        // The common fault is a handler that threw, not a render that cannot succeed: the tree is
        // intact, so recovering restores the app with its state rather than costing a round trip.
        using var host = RaskTestHost.Create<ThrowingApp>(diffMode: LiveDiffMode.DisabledFull);
        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var throwingId = Regex.Matches(initialHtml, "data-rask-on-click=\"(h\\d+)\"")
            .Select(m => m.Groups[1].Value)
            .First();

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = throwingId });
        var faulted = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(faulted);
        Assert.Contains("Try again", faulted, StringComparison.Ordinal);

        // Click it. Its id comes from the fallback render, so read it from the faulted payload.
        var retryId = Regex.Match(faulted!, "data-rask-on-click=\\\\?\"(h\\d+)\\\\?\"[^>]*>Try again")
            .Groups[1].Value;
        Assert.NotEmpty(retryId);

        await ws.SendJsonAsync(new { id = retryId });
        var recovered = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(recovered);
        Assert.Contains("count=", recovered, StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", recovered, StringComparison.Ordinal);
    }
}
