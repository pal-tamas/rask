using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     A live page whose sign-in ended somewhere else stops acting as that user on its next dispatch, rather than when its
///     socket happens to reconnect.
/// </summary>
/// <remarks>
///     <see cref="RevokedAuthDispatchTests" /> clears the principal by hand. These go through the
///     <see cref="ISessionRevalidator" /> seam an accounts battery provides, which is how the clearing actually happens.
/// </remarks>
public class SessionRevalidationDispatchTests
{
    [Fact]
    public async Task A_session_ended_elsewhere_signs_the_live_page_out_before_its_next_handler_runs()
    {
        var revalidator = new SwitchableRevalidator();
        using var host = CreateHost(revalidator);
        var counter = host.Server.Services.GetRequiredService<M2Counter>();

        var (ws, sessionId, handlerId) = await AttachAsync(host);
        using var socket = ws;

        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();

        Assert.Equal(1, counter.Count);

        // The session row is deleted on another device. Force the next dispatch to check rather than wait out the window.
        revalidator.Ended = true;
        host.Store.Get(sessionId)!.LastUserRevalidation = 0;

        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        var afterEnd = await ws.ReceiveTextAsync();

        Assert.Equal(1, counter.Count);
        Assert.Contains("/login", afterEnd);
        Assert.NotEqual(true, host.Store.Get(sessionId)!.Services.GetRequiredService<SessionUserProvider>()
            .Current.Identity?.IsAuthenticated);
    }

    // A socket that only ever navigates must be stopped too: a navigation mounts a page as surely as a click does.
    [Fact]
    public async Task A_session_ended_elsewhere_is_signed_out_by_its_next_navigation()
    {
        var revalidator = new SwitchableRevalidator();
        using var host = CreateHost(revalidator);
        var (ws, sessionId, _) = await AttachAsync(host);
        using var socket = ws;
        revalidator.Ended = true;
        host.Store.Get(sessionId)!.LastUserRevalidation = 0;

        await ws.SendJsonAsync(
            new { type = "navigate", path = "/m2/protected" }, ct: TestContext.Current.CancellationToken);
        var afterEnd = await ws.ReceiveTextAsync();

        Assert.Contains("/login", afterEnd);
        Assert.NotEqual(true, host.Store.Get(sessionId)!.Services.GetRequiredService<SessionUserProvider>()
            .Current.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task A_live_page_is_not_rechecked_on_every_dispatch()
    {
        var revalidator = new SwitchableRevalidator();
        using var host = CreateHost(revalidator);

        var (ws, _, handlerId) = await AttachAsync(host);
        using var socket = ws;

        for (var i = 0; i < 5; i++)
        {
            await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
            await ws.SettledAsync();
        }

        // The first dispatch checks; the rest fall inside the 30-second window.
        Assert.Equal(1, revalidator.Checks);
    }

    [Fact]
    public async Task Changed_claims_replace_the_page_s_principal()
    {
        var revalidator = new SwitchableRevalidator();
        using var host = CreateHost(revalidator);

        var (ws, sessionId, handlerId) = await AttachAsync(host);
        using var socket = ws;

        revalidator.ExtraRole = "editor";
        host.Store.Get(sessionId)!.LastUserRevalidation = 0;

        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();

        Assert.True(host.Store.Get(sessionId)!.Services.GetRequiredService<SessionUserProvider>()
            .Current.IsInRole("editor"));
    }

    private static async Task<(System.Net.WebSockets.WebSocket Socket, string SessionId, string HandlerId)> AttachAsync(
        RaskTestHost host)
    {
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.NameIdentifier, "alice")], "TestCookie"));
        var ticket = store.Issue(AuthAction.SignIn, principal, "TestCookie", "sess");
        var redeem = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { ticket, session = "sess" });
        var cookie = redeem.Headers.TryGetValues("Set-Cookie", out var values) ? values.First().Split(';')[0] : "";

        var get = new HttpRequestMessage(HttpMethod.Get, "/m2/protected");
        get.Headers.Add("Cookie", cookie);
        var response = await host.Http.SendAsync(get);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(html);
        var handler = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>[^<]*bump").Groups[1].Value;

        host.WebSockets.ConfigureRequest = req => req.Headers["Cookie"] = cookie;
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId);

        return (ws, sessionId, handler);
    }

    private static RaskTestHost CreateHost(SwitchableRevalidator revalidator) =>
        RaskTestHost.Create<M2App>(
            services =>
            {
                services.AddSingleton<M2Counter>();
                services.AddSingleton<ISessionRevalidator>(revalidator);
                services.AddAuthentication("TestCookie").AddCookie("TestCookie", o =>
                {
                    o.Cookie.Name = "TestCookie";
                    o.LoginPath = "/login";
                    o.AccessDeniedPath = "/forbidden";
                });
                services.AddAuthorization();
            },
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            });

    private sealed class SwitchableRevalidator : ISessionRevalidator
    {
        public bool Ended { get; set; }

        public string? ExtraRole { get; set; }

        public int Checks { get; private set; }

        public ValueTask<ClaimsPrincipal?> Revalidate(
            ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            Checks++;

            if (Ended)
            {
                return ValueTask.FromResult<ClaimsPrincipal?>(null);
            }

            if (ExtraRole is null)
            {
                return ValueTask.FromResult<ClaimsPrincipal?>(principal);
            }

            var identity = new ClaimsIdentity(principal.Claims, "TestCookie");
            identity.AddClaim(new Claim(ClaimTypes.Role, ExtraRole));
            return ValueTask.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(identity));
        }
    }
}
