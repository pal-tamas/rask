using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.Authentication;

public class AuthRedeemEndpointTests
{
    [Fact]
    public async Task Redeem_ValidTicket_Returns200_AndSetsCookie()
    {
        using var host = CreateHost();
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice")], "TestCookie"));
        var ticketId = store.Issue(AuthAction.SignIn, principal, "TestCookie", "session-1");

        var resp = await host.Http.PostAsJsonAsync(
            "/_rask/auth/redeem",
            new { ticket = ticketId, session = "session-1" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.Contains("Set-Cookie"));
        var setCookie = string.Join(";", resp.Headers.GetValues("Set-Cookie"));
        Assert.Contains("TestCookie", setCookie);
    }

    [Fact]
    public async Task Redeem_UnknownTicket_Returns410()
    {
        using var host = CreateHost();
        var resp = await host.Http.PostAsJsonAsync(
            "/_rask/auth/redeem",
            new { ticket = "no-such-id", session = "session-1" });

        Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
    }

    [Fact]
    public async Task Redeem_SessionMismatch_Returns410()
    {
        using var host = CreateHost();
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var ticketId = store.Issue(
            AuthAction.SignIn,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "TestCookie")),
            "TestCookie",
            "session-1");

        var resp = await host.Http.PostAsJsonAsync(
            "/_rask/auth/redeem",
            new { ticket = ticketId, session = "session-2" });

        Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
    }

    [Fact]
    public async Task Redeem_AlreadyRedeemed_Returns410()
    {
        using var host = CreateHost();
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var ticketId = store.Issue(
            AuthAction.SignIn,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "TestCookie")),
            "TestCookie",
            "session-1");

        var first = await host.Http.PostAsJsonAsync(
            "/_rask/auth/redeem",
            new { ticket = ticketId, session = "session-1" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await host.Http.PostAsJsonAsync(
            "/_rask/auth/redeem",
            new { ticket = ticketId, session = "session-1" });
        Assert.Equal(HttpStatusCode.Gone, replay.StatusCode);
    }

    [Fact]
    public async Task Redeem_SignOut_Returns200()
    {
        using var host = CreateHost();
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var ticketId = store.Issue(AuthAction.SignOut, null, "TestCookie", "session-1");

        var resp = await host.Http.PostAsJsonAsync(
            "/_rask/auth/redeem",
            new { ticket = ticketId, session = "session-1" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Redeem_MissingFields_Returns400()
    {
        using var host = CreateHost();
        var resp = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { foo = "bar" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<SignInTestApp>(
            services =>
            {
                services.AddAuthentication("TestCookie")
                    .AddCookie("TestCookie", o => { o.Cookie.Name = "TestCookie"; });
                services.AddAuthorization();
            },
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            });
}
