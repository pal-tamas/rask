using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

// End-to-end coverage of every route-guard outcome through the real Rask HTTP pipeline
// (middleware → cookie auth → RouteAuthorizationGuard → challenge/forbid), plus the cookie
// established via the genuine sign-in redeem handshake.
public class RouteGuardPipelineTests
{
    [Fact]
    public async Task An_anonymous_visitor_gets_a_public_page_with_200()
    {
        using var host = CreateHost();

        var resp = await host.Http.GetAsync("/e2e/public");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("public-content", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_anonymous_visitor_to_a_protected_page_is_challenged_to_login()
    {
        using var host = CreateHost();

        var resp = await host.Http.GetAsync("/e2e/members");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/login", resp.Headers.Location!.AbsolutePath);
        Assert.Contains("ReturnUrl", resp.Headers.Location!.Query);
    }

    [Fact]
    public async Task An_anonymous_visitor_to_an_admin_page_is_challenged_to_login()
    {
        using var host = CreateHost();

        var resp = await host.Http.GetAsync("/e2e/admin");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/login", resp.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task A_signed_in_user_gets_a_protected_page_with_200_and_their_name()
    {
        using var host = CreateHost();
        var cookie = await SignInAsync(host, "alice");

        var resp = await GetWithCookieAsync(host, "/e2e/members", cookie);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("members-content", body);
        Assert.Contains("alice", body); // SessionUserProvider seeded from the cookie principal
    }

    [Fact]
    public async Task A_signed_in_non_admin_is_forbidden_from_an_admin_page()
    {
        using var host = CreateHost();
        var cookie = await SignInAsync(host, "alice", "user");

        var resp = await GetWithCookieAsync(host, "/e2e/admin", cookie);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode); // forbid → AccessDeniedPath
        Assert.Equal("/forbidden", resp.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task An_admin_gets_an_admin_page_with_200()
    {
        using var host = CreateHost();
        var cookie = await SignInAsync(host, "root", "admin");

        var resp = await GetWithCookieAsync(host, "/e2e/admin", cookie);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("admin-content", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Signing_out_clears_the_cookie_and_loses_access()
    {
        using var host = CreateHost();
        var cookie = await SignInAsync(host, "alice");

        Assert.Equal(HttpStatusCode.OK, (await GetWithCookieAsync(host, "/e2e/members", cookie)).StatusCode);

        var cleared = await SignOutAsync(host); // ctx.SignOutAsync emits an emptied cookie
        var resp = await GetWithCookieAsync(host, "/e2e/members", cleared);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode); // back to the challenge
    }

    private static async Task<string> SignInAsync(RaskTestHost host, string name, params string[] roles)
    {
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(ClaimTypes.NameIdentifier, name) };
        foreach (var r in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestCookie"));
        var ticket = store.Issue(AuthAction.SignIn, principal, "TestCookie", "sess");
        var resp = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { ticket, session = "sess" });
        return CookieFrom(resp);
    }

    private static async Task<string> SignOutAsync(RaskTestHost host)
    {
        var store = host.Server.Services.GetRequiredService<IAuthTicketStore>();
        var ticket = store.Issue(AuthAction.SignOut, null, "TestCookie", "sess");
        var resp = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { ticket, session = "sess" });
        return CookieFrom(resp);
    }

    private static string CookieFrom(HttpResponseMessage resp) =>
        resp.Headers.TryGetValues("Set-Cookie", out var values) ? values.First().Split(';')[0] : "";

    private static Task<HttpResponseMessage> GetWithCookieAsync(RaskTestHost host, string path, string cookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(cookie))
        {
            req.Headers.Add("Cookie", cookie);
        }

        return host.Http.SendAsync(req);
    }

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<RouteGuardTestApp>(
            services =>
            {
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
