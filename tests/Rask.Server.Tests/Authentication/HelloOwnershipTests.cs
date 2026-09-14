using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;
using Rask.TestSupport;

namespace Rask.Server.Tests.Authentication;

// #1075: a hello naming a session that exists attaches only for the session's owner — or, while a
// sign-in/sign-out handoff is in flight, for the one principal that handoff's reconnect will carry.
// Everyone else is told the session is unknown, exactly as for an id that never existed.
public class HelloOwnershipTests
{
    [Fact]
    public async Task Hello_FromAnotherSignedInUser_IsAnsweredAsUnknown_AndLeavesTheSessionAlone()
    {
        using var host = CreateHost();
        var sessionId = await OpenSessionAsAsync(host, await SignInCookieAsync(host, "alice"));

        var reply = await HelloAsync(host, sessionId, await SignInCookieAsync(host, "mallory"));

        AssertSessionUnknown(reply);
        AssertStillOwnedBy(host, sessionId, "alice");
    }

    [Fact]
    public async Task Hello_Anonymous_OnASignedInSession_IsAnsweredAsUnknown()
    {
        using var host = CreateHost();
        var sessionId = await OpenSessionAsAsync(host, await SignInCookieAsync(host, "alice"));

        var reply = await HelloAsync(host, sessionId, cookie: null);

        AssertSessionUnknown(reply);
        AssertStillOwnedBy(host, sessionId, "alice");
    }

    [Fact]
    public async Task Hello_FromTheOwner_Attaches()
    {
        using var host = CreateHost();
        var alice = await SignInCookieAsync(host, "alice");
        var sessionId = await OpenSessionAsAsync(host, alice);

        host.WebSockets.ConfigureRequest = req => req.Headers["Cookie"] = alice;
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        await WaitFor.True(() => host.Store.ConnectedCount == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    // The sharp case the gate must not break: the owner is authenticated, and the handoff's reconnect
    // arrives as nobody.
    [Fact]
    public async Task SignOut_TheAnonymousReconnect_IsAdmitted()
    {
        using var host = CreateHost();
        var alice = await SignInCookieAsync(host, "alice");
        var (sessionId, ticket) = await ClickThroughHandoffAsync(host, alice, "sign-out");

        await RedeemAsync(host, ticket, sessionId, alice);
        var jar = host.Server.Services.GetRequiredService<TestCookieJar>();

        var frame = await HelloAsync(host, sessionId, jar.Cookie);

        Assert.NotNull(frame);
        Assert.Contains("user=anon", frame);
    }

    [Fact]
    public async Task SignOut_WhileTheHandoffIsInFlight_AnotherUserIsStillRefused()
    {
        using var host = CreateHost();
        var alice = await SignInCookieAsync(host, "alice");
        var (sessionId, ticket) = await ClickThroughHandoffAsync(host, alice, "sign-out");
        await RedeemAsync(host, ticket, sessionId, alice);

        // A sign-out expects an anonymous reconnect — not just anyone who is not alice.
        var reply = await HelloAsync(host, sessionId, await SignInCookieAsync(host, "mallory"));

        AssertSessionUnknown(reply);
    }

    // The handoff window opens at the redeem, which only the ticket's holder can perform — not when the
    // handler issues the ticket, which would let any anonymous holder of the id in first.
    [Fact]
    public async Task SignOut_BeforeTheTicketIsRedeemed_AnAnonymousHelloIsRefused()
    {
        using var host = CreateHost();
        var alice = await SignInCookieAsync(host, "alice");
        var (sessionId, _) = await ClickThroughHandoffAsync(host, alice, "sign-out");

        var reply = await HelloAsync(host, sessionId, cookie: null);

        AssertSessionUnknown(reply);
    }

    // A refusal must look exactly like an id that does not exist, resume record included: a stranger who
    // names a live session alongside their OWN valid record gets their own page rebuilt, as they would
    // for a dead id — not a refusal that reveals the id is live.
    [Fact]
    public async Task Hello_Refused_WithTheCallersOwnResumeRecord_RebuildsTheirPage_AsForAnUnknownId()
    {
        using var host = CreateHost();
        var aliceSession = await OpenSessionAsAsync(host, await SignInCookieAsync(host, "alice"));

        var mallory = await SignInCookieAsync(host, "mallory");
        var malloryToken = await ResumeTokenForAsync(host, mallory);

        host.WebSockets.ConfigureRequest = req => req.Headers["Cookie"] = mallory;
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = aliceSession, resume = malloryToken });

        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame);
        Assert.True(doc.RootElement.TryGetProperty("html", out var html), frame);
        Assert.Contains("user=mallory", html.GetString());
        Assert.DoesNotContain(aliceSession, html.GetString());
        AssertStillOwnedBy(host, aliceSession, "alice", connected: 1);
    }

    // Switching user: the owner is authenticated as one identity, and the handoff's reconnect carries another.
    [Fact]
    public async Task SignIn_AsSomeoneElse_TheRedeemedIdentitysReconnect_IsAdmitted()
    {
        using var host = CreateHost();
        var bob = await SignInCookieAsync(host, "bob");
        var (sessionId, ticket) = await ClickThroughHandoffAsync(host, bob, "sign-in");

        await RedeemAsync(host, ticket, sessionId, bob);
        var jar = host.Server.Services.GetRequiredService<TestCookieJar>();

        var frame = await HelloAsync(host, sessionId, jar.Cookie);

        Assert.NotNull(frame);
        Assert.Contains("user=alice", frame);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static async Task<string> SignInCookieAsync(RaskTestHost host, string name)
    {
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, name)], "TestCookie"));
        var ticket = store.Issue(AuthAction.SignIn, principal, "TestCookie", "sess");
        var resp = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { ticket, session = "sess" });
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var values));
        return values.First().Split(';')[0];
    }

    private static async Task<string> OpenSessionAsAsync(RaskTestHost host, string cookie)
    {
        var html = await GetStartAsync(host, cookie);
        return MarkupAssert.SessionId(html);
    }

    private static async Task<string> GetStartAsync(RaskTestHost host, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/start");
        request.Headers.Add("Cookie", cookie);
        var response = await host.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // Opens a session as the cookie's user, attaches as its owner, and clicks a button that starts an auth
    // handoff. Returns the ticket that handoff issued; the owner's socket is closed again, as the client does.
    private static async Task<(string SessionId, string Ticket)> ClickThroughHandoffAsync(
        RaskTestHost host, string cookie, string button)
    {
        var html = await GetStartAsync(host, cookie);
        var sessionId = MarkupAssert.SessionId(html);
        var match = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + Regex.Escape(button));
        Assert.True(match.Success, $"button '{button}' not found");

        host.WebSockets.ConfigureRequest = req => req.Headers["Cookie"] = cookie;
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.SendJsonAsync(new { id = match.Groups[1].Value });

        string? ticket = null;
        for (var i = 0; i < 8 && ticket is null; i++)
        {
            var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(frame);
            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("auth", out var auth))
            {
                ticket = auth.GetProperty("ticket").GetString();
            }
        }

        Assert.NotNull(ticket);
        await ws.CloseAndAwaitServerCleanupAsync();
        return (sessionId, ticket);
    }

    private static async Task RedeemAsync(RaskTestHost host, string ticket, string sessionId, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket, session = sessionId })
        };
        request.Headers.Add("Cookie", cookie);
        var response = await host.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Connects with the given cookie (or none) and sends a hello; returns the first text frame, if any.
    private static async Task<string?> HelloAsync(RaskTestHost host, string sessionId, string? cookie)
    {
        host.WebSockets.ConfigureRequest = req =>
        {
            if (cookie is not null)
            {
                req.Headers["Cookie"] = cookie;
            }
        };
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        return await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
    }

    private static void AssertSessionUnknown(string? reply)
    {
        Assert.NotNull(reply);
        using var doc = JsonDocument.Parse(reply);
        Assert.Equal("session", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("unknown", doc.RootElement.GetProperty("status").GetString());
    }

    private static void AssertStillOwnedBy(RaskTestHost host, string sessionId, string name, int connected = 0)
    {
        var session = host.Store.Peek(sessionId);
        Assert.NotNull(session);
        Assert.Equal(name, session.Services.GetRequiredService<SessionUserProvider>().Current.Identity?.Name);
        Assert.Equal(connected, host.Store.ConnectedCount);
    }

    // A reconnect always emits a frame, and a session's first payload always carries a resume record, so
    // the second hello on a fresh session is the cheapest way to take a record off the wire.
    private static async Task<string> ResumeTokenForAsync(RaskTestHost host, string cookie)
    {
        var sessionId = await OpenSessionAsAsync(host, cookie);
        host.WebSockets.ConfigureRequest = req => req.Headers["Cookie"] = cookie;
        using (var first = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None))
        {
            await first.SendJsonAsync(new { type = "hello", session = sessionId });
            await WaitFor.True(() => host.Store.ConnectedCount == 1, TimeSpan.FromSeconds(5));
            await first.CloseAndAwaitServerCleanupAsync();
        }

        using var second = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await second.SendJsonAsync(new { type = "hello", session = sessionId });
        var frame = await second.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame);
        var token = doc.RootElement.GetProperty("resume").GetString();
        Assert.NotNull(token);
        await second.CloseAndAwaitServerCleanupAsync();
        return token;
    }

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<SignInTestApp>(
            services =>
            {
                services.AddSingleton<TestCookieJar>();
                services.AddAuthentication("TestCookie")
                    .AddCookie("TestCookie", o =>
                    {
                        o.Cookie.Name = "TestCookie";
                        o.Cookie.SameSite = SameSiteMode.Lax;
                    });
                services.AddAuthorization();
            },
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                // Capture the Set-Cookie the redeem issues, for replay on the reconnect (TestServer does
                // not bridge cookies between its HttpClient and its WebSocket client).
                app.Use(async (ctx, next) =>
                {
                    await next();
                    if (ctx.Request.Path.StartsWithSegments("/_rask/auth/redeem")
                        && ctx.Response.Headers.TryGetValue("Set-Cookie", out var values)
                        && values.FirstOrDefault(v => v?.StartsWith("TestCookie=", StringComparison.Ordinal) == true)
                            is { } v)
                    {
                        var semi = v.IndexOf(';');
                        ctx.RequestServices.GetRequiredService<TestCookieJar>().Cookie = semi < 0 ? v : v[..semi];
                    }
                });
            });
}
