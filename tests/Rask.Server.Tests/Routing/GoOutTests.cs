using System.Net;
using System.Text.Json;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// Go.Out("/tenants"): a page of this site the app does not render — the old application beside a new one mapped
// under a path base. The address is used as written, and the browser loads it as a page.
public sealed class GoOutTests
{
    [Fact]
    public async Task A_handler_that_goes_out_sends_the_browser_a_full_page_navigation_and_renders_nothing_more()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        var frames = await redirects.Click("out");

        AssertLeavesFor(Assert.Single(frames), "/tenants");
    }

    [Fact]
    public async Task A_link_to_a_page_that_goes_out_as_it_mounts_leaves_without_showing_the_page()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        var frames = await redirects.Navigate("/redirect/out");

        AssertLeavesFor(Assert.Single(frames), "/tenants");
    }

    [Fact]
    public async Task A_page_that_loads_and_then_goes_out_leaves_once_it_has_loaded()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);

        await redirects.Send(new { type = "navigate", path = "/redirect/late-out", query = "" });
        var leaving = await redirects.Ws.ReceiveUntilAsync(
            f => f.Contains("\"location\"", StringComparison.Ordinal), "the frame that sends the browser out of the app");

        AssertLeavesFor(leaving, "/tenants");
    }

    private static void AssertLeavesFor(string frame, string url)
    {
        using var doc = JsonDocument.Parse(frame);
        Assert.Equal("location", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(url, doc.RootElement.GetProperty("url").GetString());
        Assert.True(doc.RootElement.GetProperty("outside").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("replace").GetBoolean());
    }
}

// The path base is one value for the whole process, so the suites that map an app under one take turns.
[Collection("ScopedAssets")]
public sealed class GoOutUnderAPathBaseTests
{
    [Fact]
    public async Task A_first_request_under_a_path_base_is_answered_with_a_302_to_the_address_as_written()
    {
        using var host = RaskTestHost.Create<RedirectingApp>(pathBase: "/uj");

        var response = await host.Http.GetAsync("/uj/redirect/out", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/tenants", response.Headers.Location?.OriginalString);
    }
}
