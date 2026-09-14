using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     A hello names a session by id. Whoever sends it has to be the user that session belongs to.
/// </summary>
/// <remarks>
///     <para>
///         The id is the only thing tying a connection to a session, and it travels in the page HTML. A
///         leaked one — a shared log, a screenshot, a proxy that records URLs — used to be enough to drive
///         a signed-in stranger's page: the hello attached, and then overwrote the session's principal with
///         the connecting one, so the attacker's own identity drove the victim's tree (#1075). Upload and
///         download already refused that (<c>SameSessionUser</c>); the socket did not.
///     </para>
///     <para>
///         A refusal answers <c>session/unknown</c>, the same thing an id this host never had answers, so a
///         prober cannot use the difference to learn which ids exist.
///     </para>
/// </remarks>
public sealed class HelloSameUserTests
{
    private const string UserHeader = "X-Test-User";

    /// <summary>Signs the request in as whoever the header names; anonymous without it.</summary>
    private static void StampUser(IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue(UserHeader, out var name) && !string.IsNullOrEmpty(name))
            {
                ctx.User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Name, name!)], "TestCookie"));
            }

            await next(ctx);
        });

    private static RaskTestHost NewHost() =>
        RaskTestHost.Create<NoOpApp>(configureMiddleware: StampUser);

    /// <summary>Mints a session over the GET, as <paramref name="user" /> when one is named.</summary>
    private static async Task<string> StartSessionAsync(RaskTestHost host, string? user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        using var response = await host.Http.SendAsync(request);
        return MarkupAssert.SessionId(await response.Content.ReadAsStringAsync());
    }

    private static async Task<System.Net.WebSockets.WebSocket> ConnectAsync(RaskTestHost host, string? user)
    {
        if (user is not null)
        {
            host.WebSockets.ConfigureRequest = request => request.Headers[UserHeader] = user;
        }

        return await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
    }

    [Fact]
    public async Task Another_signed_in_user_cannot_attach_to_a_session_they_do_not_own()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host, "alice");

        using var ws = await ConnectAsync(host, "bob");
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var reply = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(reply);
        Assert.Contains("\"type\":\"session\"", reply, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"unknown\"", reply, StringComparison.Ordinal);

        // Refused means refused: the session is still alice's, and bob's connection never counted.
        Assert.Equal(0, host.Store.ConnectedCount);
    }

    [Fact]
    public async Task The_owner_attaches_to_their_own_session()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host, "alice");

        using var ws = await ConnectAsync(host, "alice");
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        // Nothing is pushed when nothing changed, so the attach is observed through the store rather than
        // by waiting for a frame that correctly never comes.
        await WaitForConnectedAsync(host, 1);
        Assert.Equal(1, host.Store.ConnectedCount);
    }

    /// <summary>
    ///     An anonymous session is matched by anyone: nobody owns it, and the unguessable id is the only
    ///     authority there — the same posture upload and download take.
    /// </summary>
    [Fact]
    public async Task An_anonymous_session_is_attachable_by_anyone()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host, user: null);

        using var ws = await ConnectAsync(host, "bob");
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        await WaitForConnectedAsync(host, 1);
        Assert.Equal(1, host.Store.ConnectedCount);
    }

    /// <summary>
    ///     An anonymous connection cannot take over a session that belongs to someone: signing out of the
    ///     attacker's own session is not a way in.
    /// </summary>
    [Fact]
    public async Task An_anonymous_connection_cannot_attach_to_an_owned_session()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host, "alice");

        using var ws = await ConnectAsync(host, user: null);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var reply = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(reply);
        Assert.Contains("\"type\":\"session\"", reply, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"unknown\"", reply, StringComparison.Ordinal);
        Assert.Equal(0, host.Store.ConnectedCount);
    }

    /// <summary>
    ///     The host the sign-in and sign-out journeys need: the header still says who a request is, and cookie
    ///     authentication is registered so the redeem endpoint has a scheme to sign in and out of.
    /// </summary>
    private static RaskTestHost NewAuthHost() =>
        RaskTestHost.Create<NoOpApp>(
            configureServices: services => services.AddAuthentication("TestCookie").AddCookie("TestCookie"),
            configureMiddleware: StampUser);

    private static async Task RedeemAsync(RaskTestHost host, AuthAction action, string sessionId, string? toUser)
    {
        var principal = toUser is null
            ? null
            : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, toUser)], "TestCookie"));
        var ticket = host.Services.GetRequiredService<IAuthTicketStore>()
            .Issue(action, principal, "TestCookie", sessionId);

        using var response = await host.Http.PostAsJsonAsync(
            "/_rask/auth/redeem", new { ticket, session = sessionId });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    ///     Signing out is a reconnect: the redeem clears the cookie, then the tab says hello again — anonymous, to a
    ///     session that recorded the signed-in owner. Refusing that (the rule above, applied blindly) turned every
    ///     sign-out on a live page into "your session timed out".
    /// </summary>
    [Fact]
    public async Task Signing_out_reconnects_to_the_same_session()
    {
        using var host = NewAuthHost();
        var sessionId = await StartSessionAsync(host, "alice");

        await RedeemAsync(host, AuthAction.SignOut, sessionId, toUser: null);

        using var ws = await ConnectAsync(host, user: null);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        await WaitForConnectedAsync(host, 1);
        Assert.Equal(1, host.Store.ConnectedCount);
    }

    /// <summary>
    ///     Switching account names exactly who the reconnect will be: the new user attaches, and nobody else does in
    ///     the meantime — the redeem is not an opening for whoever holds the id.
    /// </summary>
    [Fact]
    public async Task Switching_account_lets_the_new_user_reconnect_and_nobody_else()
    {
        using var host = NewAuthHost();
        var sessionId = await StartSessionAsync(host, "alice");

        await RedeemAsync(host, AuthAction.SignIn, sessionId, toUser: "bob");

        using (var stranger = await ConnectAsync(host, "carol"))
        {
            await stranger.SendJsonAsync(new { type = "hello", session = sessionId });
            var reply = await stranger.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(reply);
            Assert.Contains("\"status\":\"unknown\"", reply, StringComparison.Ordinal);
        }

        using var ws = await ConnectAsync(host, "bob");
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        await WaitForConnectedAsync(host, 1);
        Assert.Equal(1, host.Store.ConnectedCount);
    }

    private static async Task WaitForConnectedAsync(RaskTestHost host, int expected)
    {
        for (var i = 0; i < 100 && host.Store.ConnectedCount != expected; i++)
        {
            await Task.Delay(20);
        }
    }
}
