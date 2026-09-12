using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
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

    private static async Task WaitForConnectedAsync(RaskTestHost host, int expected)
    {
        for (var i = 0; i < 100 && host.Store.ConnectedCount != expected; i++)
        {
            await Task.Delay(20);
        }
    }
}
