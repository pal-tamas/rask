using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;
using Rask.TestSupport;

namespace Rask.Server.Tests.Routing;

// "Record not found, back to the list": a page that loads and THEN calls Go(). Its placeholder was on screen while
// it loaded, so the destination replaces it — in the document and in the history, where the page that sent the
// reader on is not a place to go back to.
public sealed class RedirectAfterAwaitTests
{
    private const string Home = "/redirect/home";

    [Fact]
    public async Task A_link_to_a_page_that_loads_and_then_redirects_lands_on_its_destination_replacing_its_address()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        await redirects.Send(new { type = "navigate", path = "/redirect/late", query = "" });
        var loading = await redirects.Ws.ReceiveNavigationToAsync("/redirect/late");
        var landing = await redirects.Ws.ReceiveNavigationToAsync(Home);

        Assert.Equal("push", HistoryAction(loading));
        Assert.Contains("late-content", loading);
        Assert.Equal("replace", HistoryAction(landing));
        Assert.Contains("home-content", landing);
        Assert.Contains(">Home</title>", Html(landing));
        Assert.Contains("crumb:Home", Html(landing));
    }

    [Theory]
    [InlineData("to-late")]
    [InlineData("save-then-late")]
    public async Task A_handler_that_goes_to_a_page_that_loads_and_then_redirects_lands_on_its_destination(string button)
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        await redirects.Press(button);
        var landing = await redirects.Ws.ReceiveNavigationToAsync(Home);

        Assert.Equal("replace", HistoryAction(landing));
        Assert.Contains("home-content", landing);
        Assert.DoesNotContain("late-content", landing);
    }

    [Fact]
    public async Task A_first_request_whose_page_redirects_within_the_time_it_waits_is_answered_with_a_302()
    {
        using var host = RedirectingSession.Host(LiveDiffMode.Auto);

        var response = await host.Http.GetAsync("/redirect/late", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(Home, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_page_whose_load_outlasts_the_first_response_redirects_over_the_connection_once_it_is_open()
    {
        RedirectGate.Reset();
        await using var redirects = await RedirectingSession.Open(
            "/redirect/gated", LiveDiffMode.DisabledFull, quiescence: TimeSpan.FromMilliseconds(1));

        RedirectGate.Open();
        var landing = await redirects.Ws.ReceiveNavigationToAsync(Home);

        Assert.Contains("gated-content", redirects.FirstHtml);
        Assert.Equal("replace", HistoryAction(landing));
        Assert.Contains("home-content", landing);
    }

    [Fact]
    public async Task A_page_that_redirects_between_the_first_response_and_the_hello_tells_the_address_on_attach()
    {
        RedirectGate.Reset();
        await using var redirects = await RedirectingSession.Open(
            "/redirect/gated", LiveDiffMode.DisabledFull, quiescence: TimeSpan.FromMilliseconds(1), hello: false);
        var route = redirects.Session.Services.GetRequiredService<RouteState>();

        RedirectGate.Open();
        await WaitFor.True(() => route.Path == Home, "the session's route to move to the destination");
        await redirects.Hello();
        var landing = await redirects.Ws.ReceiveNavigationToAsync(Home);

        Assert.Equal("replace", HistoryAction(landing));
        Assert.Contains("home-content", landing);
    }

    [Fact]
    public async Task A_page_the_reader_has_left_by_the_time_its_load_ends_sends_them_nowhere()
    {
        RedirectGate.Reset();
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);
        var route = redirects.Session.Services.GetRequiredService<RouteState>();
        await redirects.Navigate("/redirect/gated");
        await redirects.Navigate("/redirect/start");

        RedirectGate.Open();
        await RedirectGate.HasAsked;
        var frames = await redirects.Ws.SettledAsync();

        Assert.DoesNotContain(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal("/redirect/start", route.Path);
    }

    [Fact]
    public async Task A_page_that_loads_and_then_redirects_into_a_guarded_layout_sends_an_anonymous_reader_to_sign_in()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);
        var constructedBefore = RedirectVaultPage.Constructed;

        await redirects.Send(new { type = "navigate", path = "/redirect/late-vault", query = "" });
        var landing = await redirects.Ws.ReceiveNavigationToAsync("/login?returnUrl=%2Fredirect-vault%2Finside");

        Assert.Equal("replace", HistoryAction(landing));
        Assert.DoesNotContain("vault-content", landing);
        Assert.Equal(constructedBefore, RedirectVaultPage.Constructed);
    }

    [Fact]
    public async Task A_session_rebuilt_on_a_page_that_redirects_as_it_mounts_lands_on_its_destination()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);
        var gone = redirects.Session;
        gone.Services.GetRequiredService<RedirectChoice>().Chosen = true;
        var work = await redirects.Navigate("/redirect/work");
        await redirects.Press("keep");
        var record = Resume(await redirects.Ws.ReceiveUntilAsync(f => Resume(f) is not null, "a frame with a resume record"));
        await redirects.Server.Store.RemoveAsync(gone.Id);

        await using var again = await LiveTestConnection.OpenAsync(
            redirects.Server, LiveTransportKind.WebSocket, gone.Id, record);
        var landing = await again.ReceiveUntilAsync(
            f => LiveFrames.HistoryUrl(f) == Home, "the frame that moves the rebuilt session to the destination");

        Assert.Contains(work, f => f.Contains("work-content", StringComparison.Ordinal));
        Assert.Equal("replace", HistoryAction(landing));
        Assert.Contains("home-content", landing);
        Assert.DoesNotContain("work-content", landing);
    }

    private static string? Resume(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("resume", out var r)
            ? r.GetString()
            : null;
    }

    private static string? HistoryAction(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.GetProperty("history").GetProperty("action").GetString();
    }

    private static string Html(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}
