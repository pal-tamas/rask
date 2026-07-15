using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Authentication;

// M2: a server-side event handler on a protected page must not run once the session principal's
// authorization has been revoked mid-session — the dispatch path re-checks the route guard before
// invoking the handler and ships a challenge redirect instead.
public class RevokedAuthDispatchTests
{
    [Fact]
    public async Task Handler_AfterAuthRevoked_IsNotInvoked_AndRedirectsToLogin()
    {
        using var host = CreateHost();
        var counter = host.Server.Services.GetRequiredService<M2Counter>();

        var cookie = await SignInAsync(host, "alice");

        // Authenticated GET → an authorized session for the protected page.
        var getReq = new HttpRequestMessage(HttpMethod.Get, "/m2/protected");
        getReq.Headers.Add("Cookie", cookie);
        var getResp = await host.Http.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var html = await getResp.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(html);
        var handlerId = ExtractHandlerId(html, "bump");

        // Carry the auth cookie onto the WS upgrade so the socket attaches as alice.
        host.WebSockets.ConfigureRequest = req => req.Headers["Cookie"] = cookie;
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // Sanity: while authorized, the handler runs.
        await ws.SendJsonAsync(new { id = handlerId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, counter.Count);

        // Revoke authorization on the live session (e.g. signed out in another tab / cookie expired
        // and re-resolved anonymous on a reconnect).
        host.Store.Get(sessionId)!.Services.GetRequiredService<SessionUserProvider>().Clear();

        // Fire the same handler again: it must be skipped and a challenge redirect emitted.
        await ws.SendJsonAsync(new { id = handlerId });
        var afterRevoke = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, counter.Count); // handler did NOT run a second time
        Assert.NotNull(afterRevoke);
        Assert.Contains("/login", afterRevoke!);
    }

    private static async Task<string> SignInAsync(RaskTestHost host, string name)
    {
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(ClaimTypes.NameIdentifier, name) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestCookie"));
        var ticket = store.Issue(AuthAction.SignIn, principal, "TestCookie", "sess");
        var resp = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { ticket, session = "sess" });
        return resp.Headers.TryGetValues("Set-Cookie", out var values) ? values.First().Split(';')[0] : "";
    }

    private static string ExtractHandlerId(string html, string buttonText)
    {
        var match = Regex.Match(
            html,
            "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>[^<]*" + Regex.Escape(buttonText));
        Assert.True(match.Success, $"button with text '{buttonText}' not found");
        return match.Groups[1].Value;
    }

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<M2App>(
            services =>
            {
                services.AddSingleton<M2Counter>();
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
}

public sealed class M2Counter
{
    public int Count;
}

public sealed class M2App : Component
{
    protected override Component? Render() =>
        [Doctype(), Html("en")[Head()[Title()["m2"]], Body()[Router()]]];
}

[Route("/m2/protected")]
[Authorize]
public sealed class M2ProtectedPage(M2Counter counter) : Component
{
    protected override Component? Render() =>
        Div(Id: "m2")[
            Span()[$"count={counter.Count}"],
            Button(OnClick: () => counter.Count++)["bump"]
        ];
}
