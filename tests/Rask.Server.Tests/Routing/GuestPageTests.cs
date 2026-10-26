using System.Net;
using System.Text.Json;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// [Guest(Then = "/redirect/home")]: a sign-in page a reader already signed in never sees — a 302 on the first
// request, and in an open session the destination in its place, replacing the address. Nobody signed in sees it.
public sealed class GuestPageTests
{
    private const string Home = "/redirect/home";

    [Fact]
    public async Task A_first_request_for_a_guest_page_by_a_signed_in_reader_is_a_302_to_its_then()
    {
        using var host = RedirectingSession.Host(LiveDiffMode.Auto);
        var cookie = await RedirectingSession.SignIn(host, "alice");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/guest");
        request.Headers.Add("Cookie", cookie);
        var constructedBefore = RedirectGuestPage.Constructed;

        var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(Home, response.Headers.Location?.OriginalString);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(constructedBefore, RedirectGuestPage.Constructed);
    }

    [Fact]
    public async Task A_first_request_for_a_guest_page_by_a_reader_not_signed_in_shows_it()
    {
        using var host = RedirectingSession.Host(LiveDiffMode.Auto);

        var response = await host.Http.GetAsync("/redirect/guest", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("guest-content", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_link_to_a_guest_page_while_signed_in_lands_on_its_then_in_place_of_it()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull, signedInAs: "alice");
        var constructedBefore = RedirectGuestPage.Constructed;

        await redirects.Send(new { type = "navigate", path = "/redirect/guest", query = "" });
        var landing = await redirects.Ws.ReceiveNavigationToAsync(Home);

        Assert.Equal("replace", HistoryAction(landing));
        Assert.Contains("home-content", landing);
        Assert.DoesNotContain("guest-content", landing);
        Assert.Equal(constructedBefore, RedirectGuestPage.Constructed);
    }

    [Fact]
    public async Task A_link_to_a_guest_page_while_not_signed_in_shows_it()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        var frames = await redirects.Navigate("/redirect/guest");

        Assert.Contains(frames, static f => LiveFrames.HistoryUrl(f) == "/redirect/guest" && f.Contains("guest-content", StringComparison.Ordinal));
    }

    private static string? HistoryAction(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.GetProperty("history").GetProperty("action").GetString();
    }
}
