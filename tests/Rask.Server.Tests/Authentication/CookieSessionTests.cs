using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

public class CookieSessionTests
{
    private const string Redeem = "/_rask/auth/redeem";

    [Fact]
    public async Task A_token_sign_in_hands_the_browser_a_ticket_that_loads_the_page_it_asked_for()
    {
        using var host = CreateHost();
        var (ws, page) = await AttachAsync(host);
        using var socket = ws;

        await ws.SendJsonAsync(new { id = HandlerId(page, "token-sign-in") }, ct: TestContext.Current.CancellationToken);
        using var frame = JsonDocument.Parse(await ws.ReceiveTextAsync());

        var auth = frame.RootElement.GetProperty("auth");
        Assert.Equal(JsonValueKind.String, auth.GetProperty("ticket").ValueKind);
        Assert.Equal("/orders?page=2", auth.GetProperty("returnUrl").GetString());
        Assert.True(auth.GetProperty("load").GetBoolean());
        Assert.DoesNotContain(TokenCookieHandler.AliceToken, frame.RootElement.GetRawText());
    }

    [Fact]
    public async Task A_return_address_on_another_origin_falls_back_to_the_root()
    {
        using var host = CreateHost();
        var (ws, page) = await AttachAsync(host);
        using var socket = ws;

        await ws.SendJsonAsync(new { id = HandlerId(page, "token-sign-in-elsewhere") }, ct: TestContext.Current.CancellationToken);
        using var frame = JsonDocument.Parse(await ws.ReceiveTextAsync());

        Assert.Equal("/", frame.RootElement.GetProperty("auth").GetProperty("returnUrl").GetString());
    }

    [Fact]
    public async Task A_token_ticket_redeems_once_into_the_session_cookie()
    {
        using var host = CreateHost();
        var ticket = IssueToken(host);

        var redeemed = await PostAsync(host, ticket);

        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        Assert.Equal("no-store", redeemed.Headers.CacheControl?.ToString());
        var cookie = Assert.Single(redeemed.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{TokenCookieHandler.CookieName}={TokenCookieHandler.AliceToken};", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_replayed_token_ticket_is_refused_and_sets_no_cookie()
    {
        using var host = CreateHost();
        var ticket = IssueToken(host);
        await PostAsync(host, ticket);

        var replay = await PostAsync(host, ticket);

        Assert.Equal(HttpStatusCode.Gone, replay.StatusCode);
        Assert.False(replay.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task An_expired_token_ticket_is_refused_and_sets_no_cookie()
    {
        var clock = new StoppedClock();
        using var host = CreateHost(services => services.AddSingleton<IAuthTicketStore>(new AuthTicketStore(clock)));
        var ticket = IssueToken(host);

        clock.Advance(AuthTicketStore.Ttl);
        var late = await PostAsync(host, ticket);

        Assert.Equal(HttpStatusCode.Gone, late.StatusCode);
        Assert.False(late.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    [InlineData("none")]
    public async Task A_redeem_a_browser_says_came_from_elsewhere_is_refused_and_leaves_the_ticket(string site)
    {
        using var host = CreateHost();
        var ticket = IssueToken(host);

        var forged = await PostAsync(host, ticket, request => request.Headers.Add("Sec-Fetch-Site", site));

        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.False(forged.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ticket)).StatusCode);
    }

    [Fact]
    public async Task A_redeem_the_browser_says_came_from_the_page_itself_is_taken()
    {
        using var host = CreateHost();
        var ticket = IssueToken(host);

        var own = await PostAsync(host, ticket, request => request.Headers.Add("Sec-Fetch-Site", "same-origin"));

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task Signing_out_clears_the_session_cookie_under_a_scheme_that_cannot_sign_out()
    {
        using var host = CreateHost();
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var ticket = store.Issue(AuthAction.SignOut, null, null, "session-1");

        var signedOut = await PostAsync(host, ticket);

        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
        var cookie = Assert.Single(signedOut.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{TokenCookieHandler.CookieName}=;", cookie, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("domain=.example.com", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_session_cookie_is_HttpOnly_Secure_Lax_site_wide_and_lasts_its_lifetime()
    {
        var clock = new StoppedClock();
        var session = new CookieSession(
            new CookieSessionOptions { Name = "app-token", Domain = ".example.com", Lifetime = TimeSpan.FromDays(7) }, clock);
        var http = new DefaultHttpContext();

        session.Write(http.Response, "t0ken");

        var cookie = http.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        Assert.StartsWith("app-token=t0ken;", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/;", cookie + ";", StringComparison.Ordinal);
        Assert.Contains("domain=.example.com", cookie, StringComparison.Ordinal);
        Assert.Contains("expires=thu, 08 jan 2026 00:00:00 gmt", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_cookie_with_no_lifetime_ends_with_the_browser()
    {
        var session = new CookieSession(new CookieSessionOptions(), new StoppedClock());
        var http = new DefaultHttpContext();

        session.Write(http.Response, "t0ken");

        var cookie = http.Response.Headers.SetCookie.ToString();
        Assert.StartsWith("rask.session=t0ken;", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("expires", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_token_sign_in_on_a_host_without_a_session_cookie_names_the_call_that_adds_one()
    {
        var signIn = new AuthSignIn();

        using (signIn.EnterHandler())
        {
            var refused = Assert.Throws<InvalidOperationException>(() => signIn.SignInWithToken("t0ken").GetAwaiter().GetResult());

            Assert.Contains("AddRaskCookieSession", refused.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("semi;colon")]
    public void A_name_that_is_no_cookie_name_is_refused_at_start(string name)
    {
        var options = new CookieSessionOptions { Name = name };

        var refused = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Rask:CookieSession:Name", refused.Message, StringComparison.Ordinal);
    }

    private static string IssueToken(RaskTestHost host) =>
        host.Server.Services.GetRequiredService<IAuthTicketStore>()
            .Issue(AuthAction.SignIn, null, null, "session-1", token: TokenCookieHandler.AliceToken);

    private static Task<HttpResponseMessage> PostAsync(
        RaskTestHost host, string ticket, Action<HttpRequestMessage>? shape = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Redeem)
        {
            Content = JsonContent.Create(new { ticket, session = "session-1" }),
        };
        shape?.Invoke(request);
        return host.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<(System.Net.WebSockets.WebSocket Ws, string Page)> AttachAsync(RaskTestHost host)
    {
        var page = await host.Http.GetStringAsync("/start", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(page);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        return (ws, page);
    }

    private static string HandlerId(string html, string buttonText)
    {
        var match = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + Regex.Escape(buttonText) + "<");
        Assert.True(match.Success, $"button '{buttonText}' not found in the page");
        return match.Groups[1].Value;
    }

    private static RaskTestHost CreateHost(Action<IServiceCollection>? more = null) =>
        RaskTestHost.Create<TokenSignInTestApp>(
            services =>
            {
                services.AddAuthentication(TokenCookieHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TokenCookieHandler>(TokenCookieHandler.Scheme, null);
                services.AddAuthorization();
                services.AddRaskCookieSession(o =>
                {
                    o.Name = TokenCookieHandler.CookieName;
                    o.Domain = ".example.com";
                });
                more?.Invoke(services);
            },
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            });
}
