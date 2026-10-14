using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;
using Rask.Server.Tests.WebSockets;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     An app mounted under a path base beside a site that owns the sign-in page — no page of its own at
///     <c>/login</c>. A session whose sign-in ended is sent to the site's sign-in page as a page load, with the full
///     address of the page as the way back, because the app's router has nothing there to show.
/// </summary>
/// <remarks>
///     In the route-registry collection: hiding the sign-in page means replacing this assembly's routes, which every
///     other test here reads.
/// </remarks>
[Collection(MountNavigationTests.Name)]
public sealed class SignInOutsideTheAppTests : IDisposable
{
    private const string UserHeader = "X-Test-User";

    public void Dispose() => __RaskRoutesRegistry.RefreshAll();

    [Fact]
    public async Task An_event_after_the_sign_in_ended_loads_the_site_s_sign_in_page_with_the_page_s_full_address()
    {
        HideTheSignInPage();
        using var host = CreateHost();
        var page = await OpenAsync(host, "/uj/open?tab=2");
        using var ws = await AttachAsync(host, page.SessionId);
        host.Services.GetRequiredService<Directory>().SignedOut = true;

        await ws.SendJsonAsync(new { id = page.Save }, TestContext.Current.CancellationToken);
        var frame = await ws.ReceiveUntilAsync(IsLocation, "the frame that loads the sign-in page");

        AssertLocation(frame, "/login?returnUrl=%2Fuj%2Fopen%3Ftab%3D2", outside: true);
        Assert.Empty(host.Services.GetRequiredService<SessionStaysItsUsersTests.Ledger>().Saves);
    }

    [Fact]
    public async Task A_navigation_after_the_sign_in_ended_loads_the_site_s_sign_in_page_with_where_it_was_going()
    {
        HideTheSignInPage();
        using var host = CreateHost();
        var page = await OpenAsync(host, "/uj/open");
        using var ws = await AttachAsync(host, page.SessionId);
        host.Services.GetRequiredService<Directory>().SignedOut = true;

        await ws.SendJsonAsync(new { type = "navigate", path = "/kept" }, TestContext.Current.CancellationToken);
        var frame = await ws.ReceiveUntilAsync(IsLocation, "the frame that loads the sign-in page");

        AssertLocation(frame, "/login?returnUrl=%2Fuj%2Fkept", outside: true);
    }

    [Fact]
    public async Task An_app_with_its_own_sign_in_page_still_shows_it_in_place_with_the_way_back_inside_the_app()
    {
        using var host = CreateHost();
        var page = await OpenAsync(host, "/uj/open");
        using var ws = await AttachAsync(host, page.SessionId);
        host.Services.GetRequiredService<Directory>().SignedOut = true;

        await ws.SendJsonAsync(new { id = page.Save }, TestContext.Current.CancellationToken);
        var frame = await ws.ReceiveNavigationToAsync("/login?returnUrl=%2Fopen");

        Assert.Contains("sign in here", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tab_that_lost_its_user_reloads_into_the_site_s_sign_in_page_with_the_page_s_full_address()
    {
        HideTheSignInPage();
        using var host = CreateHost();

        using var response = await ReloadAfterTheSignInEndedAsync(host, "/uj/open?tab=2");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fuj%2Fopen%3Ftab%3D2", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_tab_that_lost_its_user_reloads_into_the_app_s_own_sign_in_page_under_its_path_base()
    {
        using var host = CreateHost();

        using var response = await ReloadAfterTheSignInEndedAsync(host, "/uj/open?tab=2");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/uj/login?returnUrl=%2Fopen%3Ftab%3D2", response.Headers.Location?.OriginalString);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    // This assembly's routes without its page at /login: the app the site's sign-in page is beside.
    private static void HideTheSignInPage() =>
        RouteRegistry.Replace(typeof(__RaskRoutesRegistry), [
            new RouteRegistration(typeof(FollowOpenPage), "/open", null),
            new RouteRegistration(typeof(FollowKeptPage), "/kept", null),
        ]);

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<FollowApp>(
            services =>
            {
                services.AddSingleton<Directory>();
                services.AddSingleton<SessionStaysItsUsersTests.Ledger>();
                services.AddSingleton<SessionStaysItsUsersTests.Ticker>();
                services.AddSingleton<ISessionRevalidator, DirectoryRevalidator>();
            },
            app => app.Use(StampUserAsync),
            pathBase: "/uj",
            diffMode: Rask.Core.Live.LiveDiffMode.DisabledFull);

    private static Task StampUserAsync(HttpContext ctx, RequestDelegate next)
    {
        if (ctx.Request.Headers.TryGetValue(UserHeader, out var name) && !string.IsNullOrEmpty(name))
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, name!), new Claim(ClaimTypes.Name, name!)], "Test"));
        }

        return next(ctx);
    }

    private static async Task<(string SessionId, string Save)> OpenAsync(RaskTestHost host, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(UserHeader, "alice");
        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var save = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>save").Groups[1].Value;

        Assert.NotEmpty(save);
        return (MarkupAssert.SessionId(html), save);
    }

    private static async Task<WebSocket> AttachAsync(RaskTestHost host, string sessionId)
    {
        var session = host.Store.Peek(sessionId)!;
        var settled = session.AttachesSettled;
        host.WebSockets.ConfigureRequest = request => request.Headers[UserHeader] = "alice";
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has asked about its user");
        await session.LastHandlerTask;
        return ws;
    }

    private static async Task<HttpResponseMessage> ReloadAfterTheSignInEndedAsync(RaskTestHost host, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", "rask-ended=%2Fuj");
        return await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static bool IsLocation(string frame) => frame.StartsWith("{\"type\":\"location\"", StringComparison.Ordinal);

    private static void AssertLocation(string frame, string url, bool outside)
    {
        using var doc = JsonDocument.Parse(frame);
        var root = doc.RootElement;
        Assert.Equal(url, root.GetProperty("url").GetString());
        Assert.True(root.GetProperty("replace").GetBoolean());
        Assert.Equal(outside, root.TryGetProperty("outside", out var o) && o.GetBoolean());
    }

    // The app's user store, as far as these tests need it: whether the sign-in has ended.
    private sealed class Directory
    {
        public volatile bool SignedOut;
    }

    private sealed class DirectoryRevalidator(Directory directory) : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(directory.SignedOut ? null : principal);
    }
}
