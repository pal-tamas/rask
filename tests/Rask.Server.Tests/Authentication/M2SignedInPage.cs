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
///     Alice signed in to <see cref="M2ProtectedPage" /> over a socket, for the tests that ask a revalidator about her.
/// </summary>
internal static class M2SignedInPage
{
    /// <summary>A host on a stopped clock with a cookie sign-in, and the revalidator <paramref name="revalidation" /> registers.</summary>
    public static RaskTestHost CreateHost(Action<IServiceCollection> revalidation) =>
        RaskTestHost.Create<M2App>(
            services =>
            {
                services.AddSingleton<M2Counter>();
                revalidation(services);
                // How long an answer about the user is trusted is the test's to say, not the runner's speed.
                services.AddSingleton<TimeProvider>(new StoppedClock());
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

    /// <summary>Signs alice in, opens the page and attaches its socket, once the attach has asked about her.</summary>
    public static async Task<(System.Net.WebSockets.WebSocket Socket, string SessionId, string HandlerId)> AttachAsync(
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
        var session = host.Store.Peek(sessionId)!;
        var settled = session.AttachesSettled;

        host.WebSockets.ConfigureRequest = req => req.Headers["Cookie"] = cookie;
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId);

        // The socket is in place before the attach asks about its user: a test that counts the questions next
        // would otherwise count the attach's own.
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has asked about its user");
        await session.LastHandlerTask;

        return (ws, sessionId, handler);
    }
}
