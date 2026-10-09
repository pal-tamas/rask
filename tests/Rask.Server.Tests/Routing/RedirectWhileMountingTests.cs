using System.Net;
using System.Text.Json;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// A page that calls Go() as it mounts or updates — "no partner chosen, go to the partner list" — reached every
// way a page is reached on an open connection. The reader lands on the destination, with its address and its
// title, and is shown nothing of the page that sent them on.
public sealed class RedirectWhileMountingTests
{
    private const string Home = "/redirect/home";

    [Theory]
    [InlineData(LiveDiffMode.DisabledFull)]
    [InlineData(LiveDiffMode.Auto)]
    public async Task A_link_to_a_page_that_redirects_as_it_mounts_lands_on_its_destination(LiveDiffMode diffMode)
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", diffMode);

        var frames = await redirects.Navigate("/redirect/moved");

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal(Home, LiveFrames.HistoryUrl(landing));
        Assert.Equal("push", HistoryAction(landing));
        Assert.Contains("home-content", landing);
        Assert.DoesNotContain(frames, f => f.Contains("moved-content", StringComparison.Ordinal));
        Assert.DoesNotContain(frames, f => f.Contains("Application error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_title_and_the_crumb_after_a_redirect_are_the_destinations()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        var frames = await redirects.Navigate("/redirect/moved");

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Contains(">Home</title>", Html(landing));
        Assert.Contains("crumb:Home", Html(landing));
        Assert.DoesNotContain(frames, f => f.Contains("Moved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Going_back_to_a_page_that_redirects_replaces_its_entry_with_the_destination()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start");

        var frames = await redirects.Navigate("/redirect/moved", replace: true);

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal(Home, LiveFrames.HistoryUrl(landing));
        Assert.Equal("replace", HistoryAction(landing));
    }

    [Fact]
    public async Task A_chain_of_pages_that_each_redirect_lands_on_the_last_destination_in_one_frame()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        var frames = await redirects.Navigate("/redirect/first");

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal(Home, LiveFrames.HistoryUrl(landing));
        Assert.Contains("home-content", landing);
        Assert.DoesNotContain(frames, f => f.Contains("first-content", StringComparison.Ordinal));
        Assert.DoesNotContain(frames, f => f.Contains("moved-content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_reused_page_that_redirects_when_its_parameter_changes_lands_on_its_destination()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/item/1", LiveDiffMode.DisabledFull);

        var second = await redirects.Navigate("/redirect/item/2");
        var missing = await redirects.Navigate("/redirect/item/0");

        Assert.Contains(second, f => f.Contains("item-2 mounts:1", StringComparison.Ordinal));
        var landing = Assert.Single(missing, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal(Home, LiveFrames.HistoryUrl(landing));
        Assert.Contains("home-content", landing);
        Assert.DoesNotContain(missing, f => f.Contains("item-0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_redirect_to_a_path_with_a_query_carries_both_to_the_destination()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        var frames = await redirects.Navigate("/redirect/text");

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal("/redirect/home?from=text", LiveFrames.HistoryUrl(landing));
        Assert.Contains("home-content from:text", landing);
    }

    [Fact]
    public async Task A_handler_that_goes_to_a_page_that_redirects_lands_on_its_destination()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        var frames = await redirects.Click("to-moved");

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal(Home, LiveFrames.HistoryUrl(landing));
        Assert.Equal("push", HistoryAction(landing));
        Assert.Contains(">Home</title>", Html(landing));
        Assert.DoesNotContain(frames, f => f.Contains("moved-content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_redirect_into_a_guarded_layout_sends_an_anonymous_reader_to_sign_in_without_building_the_page()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);
        var constructedBefore = RedirectVaultPage.Constructed;

        var frames = await redirects.Navigate("/redirect/to-vault");

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal("/login?returnUrl=%2Fredirect-vault%2Finside", LiveFrames.HistoryUrl(landing));
        Assert.DoesNotContain(frames, f => f.Contains("vault-content", StringComparison.Ordinal));
        Assert.Equal(constructedBefore, RedirectVaultPage.Constructed);
    }

    [Fact]
    public async Task A_page_under_a_guarded_layout_redirects_a_signed_in_reader_to_the_page_beside_it()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull, signedInAs: "alice");

        var frames = await redirects.Navigate("/redirect-vault/unchosen");

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal("/redirect-vault/inside", LiveFrames.HistoryUrl(landing));
        Assert.Contains("vault-content", landing);
        Assert.DoesNotContain(frames, f => f.Contains("unchosen-content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_that_navigates_after_an_await_is_refused_and_stays_where_it_is()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        await redirects.Send(new { type = "navigate", path = "/redirect/late", query = "" });
        var landing = await redirects.Ws.ReceiveNavigationToAsync("/redirect/late");
        var refused = landing.Contains("late-content refused", StringComparison.Ordinal)
            ? landing
            : await redirects.Ws.ReceiveUntilAsync(
                f => f.Contains("late-content refused", StringComparison.Ordinal), "the page saying it was refused");

        Assert.Contains("late-content", landing);
        Assert.DoesNotContain("home-content", refused);
    }

    [Theory]
    [InlineData("/redirect/moved", Home)]
    [InlineData("/redirect/text", "/redirect/home?from=text")]
    [InlineData("/redirect/item/0", Home)]
    public async Task A_first_request_for_a_page_that_redirects_is_answered_with_a_302_to_its_destination(
        string path, string destination)
    {
        using var host = RedirectingSession.Host(LiveDiffMode.Auto);

        var response = await host.Http.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(destination, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_first_request_for_a_chain_is_answered_with_a_302_to_the_first_destination()
    {
        using var host = RedirectingSession.Host(LiveDiffMode.Auto);

        var first = await host.Http.GetAsync("/redirect/first", TestContext.Current.CancellationToken);
        var moved = await host.Http.GetAsync(first.Headers.Location, TestContext.Current.CancellationToken);
        var home = await host.Http.GetStringAsync(moved.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal("/redirect/moved", first.Headers.Location?.OriginalString);
        Assert.Equal(Home, moved.Headers.Location?.OriginalString);
        Assert.Contains(">Home</title>", home);
    }

    [Fact]
    public async Task A_first_request_redirected_into_a_guarded_layout_does_not_build_the_page_for_an_anonymous_reader()
    {
        using var host = RedirectingSession.Host(LiveDiffMode.Auto);
        var constructedBefore = RedirectVaultPage.Constructed;

        var response = await host.Http.GetAsync("/redirect/to-vault", TestContext.Current.CancellationToken);

        Assert.Equal("/redirect-vault/inside", response.Headers.Location?.OriginalString);
        Assert.Equal(constructedBefore, RedirectVaultPage.Constructed);
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
