using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

// Regression coverage for the sign-in "landing page mounts under the stale principal" bug: the auth
// handoff must defer the returnUrl navigation until the reconnect re-seeds the principal, so the
// destination page's Mount observes the redeemed identity — not the pre-SignIn one.
public class AuthDeferredNavDispatchTests
{
    [Fact]
    public async Task A_sign_in_return_url_mounts_the_destination_under_the_redeemed_identity()
    {
        using var host = CreateHost();

        var (handoff, afterReconnect) = await SignInThenReconnectAsync(host, "sign-in");

        // The client still receives the ticket + a history.replace to the destination URL, even
        // though the server-side route navigation is deferred to the reconnect.
        using var doc = JsonDocument.Parse(handoff);
        Assert.Equal("/dashboard", doc.RootElement.GetProperty("auth").GetProperty("returnUrl").GetString());
        Assert.Equal("/dashboard", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
        // The pre-reconnect render did NOT mount the destination page (route navigation deferred).
        Assert.DoesNotContain("mountUser=", handoff);
        // The deferred navigation is applied AFTER Set(wsUser), so the destination page mounts fresh
        // under the redeemed identity: Mount captured "alice", not "anon". Pre-fix, the page mounted
        // during the stale-principal pre-reconnect render and never remounted, yielding "anon".
        Assert.Contains("mountUser=alice", afterReconnect);
        Assert.DoesNotContain("mountUser=anon", afterReconnect);
    }

    // A returnUrl is checked for being local, which says nothing about who may see the page it names: the
    // route guard has to run on the reconnect's render too, or signing in as anyone opens every page.
    [Fact]
    public async Task A_sign_in_return_url_the_new_identity_may_not_see_lands_on_forbidden()
    {
        using var host = CreateHost();

        var (_, afterReconnect) = await SignInThenReconnectAsync(host, "to-admin");

        Assert.DoesNotContain("admin-only", afterReconnect);
        using var doc = JsonDocument.Parse(afterReconnect);
        Assert.Equal("/forbidden", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }

    // Clicks a sign-in button on /start, redeems the ticket, and reconnects carrying the new cookie. Returns the
    // frame that carried the ticket and the first frame after the reconnect.
    private static async Task<(string Handoff, string AfterReconnect)> SignInThenReconnectAsync(
        RaskTestHost host, string button)
    {
        var ct = TestContext.Current.CancellationToken;
        var initial = await host.Http.GetAsync("/start", ct);
        var initialHtml = await initial.Content.ReadAsStringAsync(ct);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        string handoff;

        using (var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None))
        {
            await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: ct);
            await ws.AttachedAsync(host, sessionId);

            await ws.SendJsonAsync(new { id = ExtractHandlerId(initialHtml, button) }, ct: ct);
            handoff = await ws.ReceiveUntilAsync(
                frame => frame.Contains("\"ticket\"", StringComparison.Ordinal), "the frame that hands over the sign-in ticket");

            using var doc = JsonDocument.Parse(handoff);
            var ticket = doc.RootElement.GetProperty("auth").GetProperty("ticket").GetString();
            var redeem = await host.Http.PostAsJsonAsync(
                "/_rask/auth/redeem", new { ticket, session = sessionId }, cancellationToken: ct);
            Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "auth-refresh", CancellationToken.None);
        }

        // Replay the redeem cookie onto the reconnect handshake (TestServer doesn't bridge it).
        var wsClient = host.WebSockets;
        var cookieJar = host.Server.Services.GetRequiredService<TestCookieJar>();
        wsClient.ConfigureRequest = req =>
        {
            if (cookieJar.Cookie is { } c)
            {
                req.Headers["Cookie"] = c;
            }
        };

        using var ws2 = await wsClient.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId }, ct: ct);
        var afterReconnect = await ws2.ReceiveTextAsync();

        return (handoff, afterReconnect);
    }

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<DeferredAuthNavTestApp>(
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
                // Capture the Set-Cookie from /_rask/auth/redeem for replay on the WS reconnect.
                app.Use(async (ctx, next) =>
                {
                    await next();
                    if (ctx.Response.Headers.TryGetValue("Set-Cookie", out var values))
                    {
                        var jar = ctx.RequestServices.GetRequiredService<TestCookieJar>();
                        foreach (var v in values)
                        {
                            if (v is null)
                            {
                                continue;
                            }

                            var semi = v.IndexOf(';');
                            jar.Cookie = semi < 0 ? v : v[..semi];
                        }
                    }
                });
            });

    private static string ExtractHandlerId(string html, string buttonText)
    {
        var pattern = "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" +
                      "[^<]*" + Regex.Escape(buttonText);
        var match = Regex.Match(html, pattern);
        Assert.True(match.Success, $"button with text '{buttonText}' not found in html");
        return match.Groups[1].Value;
    }
}
