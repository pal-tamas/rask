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
// destination page's OnMount observes the redeemed identity — not the pre-SignIn one.
public class AuthDeferredNavDispatchTests
{
    [Fact]
    public async Task SignInReturnUrl_MountsDestinationUnderRedeemedIdentity()
    {
        using var host = CreateHost();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var signInHandlerId = ExtractHandlerId(initialHtml, "sign-in");

        // The destination page has not mounted yet — only the anonymous /start page is live.
        Assert.DoesNotContain("mountUser=", initialHtml);

        using (var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None))
        {
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

            await ws.SendJsonAsync(new { id = signInHandlerId });
            var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(text);

            using var doc = JsonDocument.Parse(text!);
            // The client still receives the ticket + a history.replace to the destination URL, even
            // though the server-side route navigation is deferred to the reconnect.
            var authEl = doc.RootElement.GetProperty("auth");
            var ticket = authEl.GetProperty("ticket").GetString();
            Assert.Equal("/dashboard", authEl.GetProperty("returnUrl").GetString());
            Assert.Equal("/dashboard", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
            // The pre-reconnect render did NOT mount the destination page (route navigation deferred).
            Assert.DoesNotContain("mountUser=", text!);

            var redeem = await host.Http.PostAsJsonAsync(
                "/_rask/auth/redeem",
                new { ticket, session = sessionId });
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
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId });
        var afterReconnect = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(afterReconnect);

        // The deferred navigation is applied AFTER Set(wsUser), so the destination page mounts fresh
        // under the redeemed identity: OnMount captured "alice", not "anon". Pre-fix, the page mounted
        // during the stale-principal pre-reconnect render and never remounted, yielding "anon".
        Assert.Contains("mountUser=alice", afterReconnect!);
        Assert.DoesNotContain("mountUser=anon", afterReconnect!);
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
