using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Server.Authentication;
using Rask.Server.Tests.Authentication;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra pages predate framework-managed <head>

namespace Rask.Server.Tests.Prerender;

// The public-page cache through the real pipeline: cookie authentication, the route guard, the handler and
// the background renders that fill the cache. A copy is recognised by its ETag — a live response never
// carries one.
public class PrerenderEndpointTests
{
    private const string PublicPage = "/e2e/public";

    [Fact]
    public async Task APublicPage_IsServedFromItsStoredCopy_OnceItHasOne()
    {
        using var host = Host();

        using var first = await host.Http.GetAsync(PublicPage);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        // The first visit finds no copy: it is served live, and asks for one.
        Assert.Null(first.Headers.ETag);

        using var stored = await GetUntilStoredAsync(host, PublicPage);

        Assert.Contains("public-content", await stored.Content.ReadAsStringAsync());
        var cache = stored.Headers.CacheControl!;
        Assert.True(cache.Private);
        Assert.False(cache.NoStore);
        Assert.True(cache.MustRevalidate);
        Assert.Contains("Cookie", stored.Headers.Vary);
        Assert.Contains("Accept-Encoding", stored.Headers.Vary);
        // No session was built for it, and the background render's detached session is gone.
        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task ARepeatVisitCarryingTheETag_IsNotModified()
    {
        using var host = Host();
        using var stored = await GetUntilStoredAsync(host, PublicPage);

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPage);
        request.Headers.IfNoneMatch.Add(stored.Headers.ETag!);
        using var repeat = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, repeat.StatusCode);
    }

    [Fact]
    public async Task HEAD_IsAnsweredFromTheStoredCopy()
    {
        using var host = Host();
        using var stored = await GetUntilStoredAsync(host, PublicPage);

        using var head = await host.Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, PublicPage));

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(stored.Headers.ETag, head.Headers.ETag);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AQueryString_IsServedLive()
    {
        // A page can read the query and nothing can tell whether it did.
        using var host = Host();
        using var stored = await GetUntilStoredAsync(host, PublicPage);

        using var withQuery = await host.Http.GetAsync(PublicPage + "?utm_source=newsletter");

        Assert.Equal(HttpStatusCode.OK, withQuery.StatusCode);
        Assert.Null(withQuery.Headers.ETag);
    }

    [Fact]
    public async Task AGuardedPage_IsNeverStored()
    {
        using var host = Host();
        var cookie = await SignInAsync(host, "alice");

        for (var i = 0; i < 5; i++)
        {
            using var response = await GetAsync(host, "/e2e/members", cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(response.Headers.ETag);
            Assert.Contains("alice", await response.Content.ReadAsStringAsync());
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task ASignedInVisitor_GetsTheStoredCopy_OfAPageThatNeverAsksWhoTheyAre()
    {
        using var host = Host();
        using var stored = await GetUntilStoredAsync(host, PublicPage);
        var cookie = await SignInAsync(host, "alice");

        using var signedIn = await GetAsync(host, PublicPage, cookie);

        Assert.Equal(stored.Headers.ETag, signedIn.Headers.ETag);
    }

    [Fact]
    public async Task ASignedInVisitor_IsServedLive_OnAPageThatAsksWhoTheyAre()
    {
        // The stored copy was rendered for nobody, so it greets nobody. Handing it to alice would show her
        // the signed-out page — so she gets her own render instead.
        using var host = Host();
        using var stored = await GetUntilStoredAsync(host, PrerenderGreetsPage.Path);
        Assert.Contains("sign in to be greeted", await stored.Content.ReadAsStringAsync());
        var cookie = await SignInAsync(host, "alice");

        using var signedIn = await GetAsync(host, PrerenderGreetsPage.Path, cookie);

        Assert.Null(signedIn.Headers.ETag);
        Assert.Contains("hello alice", await signedIn.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AStaleCopy_IsServedWhileTheNextOneRenders()
    {
        var clock = new ManualTimeProvider();
        PrerenderClockPage.Value = "first";
        using var host = Host(services => services.AddSingleton<TimeProvider>(clock));
        using var stored = await GetUntilStoredAsync(host, PrerenderClockPage.Path);
        Assert.Contains("first", await stored.Content.ReadAsStringAsync());

        PrerenderClockPage.Value = "second";
        clock.Advance(TimeSpan.FromMinutes(2));

        // Past RevalidateAfter the visitor still gets the copy at once — never a wait on the refresh...
        using var stale = await host.Http.GetAsync(PrerenderClockPage.Path);
        Assert.NotNull(stale.Headers.ETag);
        Assert.Contains("first", await stale.Content.ReadAsStringAsync());

        // ...and the refresh it asked for lands for the requests after it.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        string body;
        do
        {
            await Task.Delay(25);
            using var next = await host.Http.GetAsync(PrerenderClockPage.Path);
            body = await next.Content.ReadAsStringAsync();
        }
        while (!body.Contains("second", StringComparison.Ordinal) && DateTime.UtcNow < deadline);

        Assert.Contains("second", body);
    }

    internal static RaskTestHost Host(Action<IServiceCollection>? services = null, string? environment = null) =>
        RaskTestHost.Create<RouteGuardTestApp>(
            configureServices: s =>
            {
                s.AddAuthentication("TestCookie").AddCookie("TestCookie", o =>
                {
                    o.Cookie.Name = "TestCookie";
                    o.LoginPath = "/login";
                    o.AccessDeniedPath = "/forbidden";
                });
                s.AddAuthorization();
                services?.Invoke(s);
            },
            configureMiddleware: app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            },
            configureServer: o =>
            {
                o.Prerender = true;
                // Delivery one stores pages that need no session; a default app's pages all keep one.
                o.RenderModes.Static = true;
            },
            environment: environment);

    /// <summary>Requests <paramref name="path" /> until it is served from a stored copy.</summary>
    internal static async Task<HttpResponseMessage> GetUntilStoredAsync(RaskTestHost host, string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var response = await host.Http.GetAsync(path);
            if (response.Headers.ETag is not null)
            {
                return response;
            }

            response.Dispose();
            Assert.True(DateTime.UtcNow < deadline, $"{path} was never served from a stored copy");
            await Task.Delay(25);
        }
    }

    private static Task<HttpResponseMessage> GetAsync(RaskTestHost host, string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return host.Http.SendAsync(request);
    }

    private static async Task<string> SignInAsync(RaskTestHost host, string name)
    {
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, name)], "TestCookie"));
        var ticket = store.Issue(AuthAction.SignIn, principal, "TestCookie", "sess");
        using var response = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { ticket, session = "sess" });
        return response.Headers.TryGetValues("Set-Cookie", out var values) ? values.First().Split(';')[0] : "";
    }
}

// Separate, because a host built for Development decides the process-wide IsDevelopment for as long as it
// lives, and the collection keeps it from running beside anything else.
[Collection("HostEnvironment")]
public class PrerenderDevelopmentTests
{
    [Fact]
    public async Task InDevelopment_NoPageIsStored()
    {
        // Every page keeps its session there so an edit repaints; a copy would freeze the page being edited.
        using var host = PrerenderEndpointTests.Host(environment: "Development");

        for (var i = 0; i < 5; i++)
        {
            using var response = await host.Http.GetAsync("/e2e/public");
            Assert.Null(response.Headers.ETag);
            await Task.Delay(50);
        }
    }
}

[Route(Path)]
public sealed partial class PrerenderGreetsPage : Component
{
    public const string Path = "/prerender/greets";

    protected override Component? Render() =>
        Div[
            Authorize
                .Authorized(user => P[$"hello {user.Identity!.Name}"])
                .NotAuthorized(P["sign in to be greeted"])
        ];
}

[Route(Path)]
public sealed partial class PrerenderClockPage : Component
{
    public const string Path = "/prerender/clock";

    // What the page shows, changed by the one test that reads it, to stand in for data that moved.
    public static volatile string Value = "unset";

    protected override Component? Render() => P[Value];
}
