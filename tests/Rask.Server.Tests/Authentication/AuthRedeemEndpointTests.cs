using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

public class AuthRedeemEndpointTests
{
    [Fact]
    public async Task Redeeming_a_valid_ticket_answers_200_and_sets_the_cookie()
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
    public async Task Redeeming_an_unknown_ticket_answers_410()
    {
        using var host = CreateHost();

        var resp = await host.Http.PostAsJsonAsync(
            "/_rask/auth/redeem",
            new { ticket = "no-such-id", session = "session-1" });

        Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
    }

    [Fact]
    public async Task Redeeming_for_a_mismatched_session_answers_410()
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
    public async Task Redeeming_an_already_redeemed_ticket_answers_410()
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
    public async Task Redeeming_a_sign_out_ticket_answers_200()
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
    public async Task A_redeem_with_missing_fields_answers_400()
    {
        using var host = CreateHost();

        var resp = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { foo = "bar" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task A_redeem_from_a_foreign_origin_answers_403()
    {
        using var host = CreateHost();
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var ticketId = store.Issue(
            AuthAction.SignIn,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "TestCookie")),
            "TestCookie",
            "session-1");

        var req = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket = ticketId, session = "session-1" })
        };
        req.Headers.Add("Origin", "https://evil.example");

        var resp = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        // The cross-origin request was bounced before the ticket was consumed — it still redeems.
        Assert.True(store.TryRedeem(ticketId, "session-1", out _));
    }

    [Fact]
    public async Task A_redeem_from_the_same_origin_succeeds()
    {
        using var host = CreateHost();
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var ticketId = store.Issue(
            AuthAction.SignIn,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "TestCookie")),
            "TestCookie",
            "session-1");

        var req = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket = ticketId, session = "session-1" })
        };
        req.Headers.Add("Origin", "http://localhost");

        var resp = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task A_redeem_with_no_origin_and_no_referer_succeeds()
    {
        // Same-origin fetch() may omit Origin entirely; with no Referer either, the ticket secrecy is
        // the authority and the request is allowed.
        using var host = CreateHost();
        var ticketId = IssueTicket(host);

        var req = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket = ticketId, session = "session-1" })
        };

        var resp = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task A_redeem_falls_back_to_a_same_host_referer_and_succeeds()
    {
        // No Origin header → fall back to Referer; same host passes.
        using var host = CreateHost();
        var ticketId = IssueTicket(host);

        var req = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket = ticketId, session = "session-1" })
        };
        req.Headers.Add("Referer", "http://localhost/login");

        var resp = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task A_redeem_with_a_foreign_referer_answers_403()
    {
        using var host = CreateHost();
        var ticketId = IssueTicket(host);

        var req = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket = ticketId, session = "session-1" })
        };
        req.Headers.Add("Referer", "https://evil.example/page");

        var resp = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task A_redeem_with_a_malformed_origin_answers_403()
    {
        // An Origin that isn't an absolute URI can't be proven same-origin → reject.
        using var host = CreateHost();
        var ticketId = IssueTicket(host);

        var req = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket = ticketId, session = "session-1" })
        };
        req.Headers.TryAddWithoutValidation("Origin", "not-a-valid-origin");

        var resp = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task A_redeem_across_schemes_on_the_same_host_succeeds()
    {
        // TLS-terminating proxy: browser Origin is https (:443) while the request reaches the app as
        // http (:80). Host matches, so redeem must still succeed (host-only comparison).
        using var host = CreateHost();
        var ticketId = IssueTicket(host);

        var req = new HttpRequestMessage(HttpMethod.Post, "/_rask/auth/redeem")
        {
            Content = JsonContent.Create(new { ticket = ticketId, session = "session-1" })
        };
        req.Headers.Add("Origin", "https://localhost");

        var resp = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private static string IssueTicket(RaskTestHost host)
    {
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        return store.Issue(
            AuthAction.SignIn,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "TestCookie")),
            "TestCookie",
            "session-1");
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
