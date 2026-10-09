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

public class AuthSignInDispatchTests
{
    [Fact]
    public async Task A_sign_in_handler_emits_an_auth_block_and_a_history_replace()
    {
        using var host = CreateHost();
        var initial = await host.Http.GetAsync("/start", TestContext.Current.CancellationToken);
        var initialHtml = await initial.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var signInHandlerId = ExtractHandlerId(initialHtml, "sign-in");

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        await ws.SendJsonAsync(new { id = signInHandlerId }, ct: TestContext.Current.CancellationToken);
        var text = await ws.ReceiveTextAsync();

        using var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("auth", out var authEl));
        Assert.Equal(JsonValueKind.String, authEl.GetProperty("ticket").ValueKind);
        Assert.Equal("/dashboard", authEl.GetProperty("returnUrl").GetString());

        Assert.True(doc.RootElement.TryGetProperty("history", out var histEl));
        Assert.Equal("replace", histEl.GetProperty("action").GetString());
        Assert.Equal("/dashboard", histEl.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Redeeming_then_reconnecting_applies_the_new_identity()
    {
        using var host = CreateHost();
        var initial = await host.Http.GetAsync("/start", TestContext.Current.CancellationToken);
        var initialHtml = await initial.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var signInHandlerId = ExtractHandlerId(initialHtml, "sign-in");

        // Initial state: anonymous
        Assert.Contains("user=anon", initialHtml);

        using (var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None))
        {
            await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
            await ws.AttachedAsync(host, sessionId);

            await ws.SendJsonAsync(new { id = signInHandlerId }, ct: TestContext.Current.CancellationToken);
            var text = await ws.ReceiveTextAsync();

            using var doc = JsonDocument.Parse(text);
            var ticket = doc.RootElement.GetProperty("auth").GetProperty("ticket").GetString();

            // Simulate the JS fetch hitting /_rask/auth/redeem; a real browser shares
            // cookies between fetch and the next WS upgrade — here we use a CookieContainer-
            // backed HttpClient to do the same.
            var redeem = await host.Http.PostAsJsonAsync(
                "/_rask/auth/redeem",
                new { ticket, session = sessionId }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);

            // Drop the WS — leaves the session in the grace window.
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "auth-refresh", CancellationToken.None);
        }

        // Reconnect: TestServer's CreateWebSocketClient does NOT carry forward the cookies
        // set on host.Http, so we manually copy the auth cookie onto the WS upgrade request.
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
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        var afterReconnect = await ws2.ReceiveTextAsync();

        // The reconnect render reflects the redeemed identity. Payload may be either a
        // full-HTML (`kind:"html"`) or a diff (`kind:"diff"` with the new text in an
        // UpdateText op's value) depending on choose-smaller heuristics — both correct.
        // Check on the raw JSON so the assertion stays robust across codec decisions.
        // (The returnUrl navigation to `/dashboard` is applied on THIS reconnect, after the
        // principal is re-seeded, so both `user=alice` and `path=/dashboard` flip here; the
        // identity is the load-bearing assertion for this test.)
        Assert.Contains("user=alice", afterReconnect);
    }

    [Fact]
    public async Task Clicks_after_the_auth_emit_are_suppressed()
    {
        using var host = CreateHost();
        var initial = await host.Http.GetAsync("/start", TestContext.Current.CancellationToken);
        var initialHtml = await initial.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var signInHandlerId = ExtractHandlerId(initialHtml, "sign-in");

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        await ws.SendJsonAsync(new { id = signInHandlerId }, ct: TestContext.Current.CancellationToken);
        await ws.ReceiveTextAsync();
        var session = host.Store.Get(sessionId)!;
        await WaitFor.True(
            () => session.SuppressEventsUntilReconnect, LiveFrames.HangCeiling, "the handoff suppresses further events");

        // Now the session is in suppressed mode. A second click should produce no payload. A suppressed
        // frame is dropped unread, so nothing the server does says it has seen this one: the wait is a
        // window, and a slow machine can only make it pass for less reason, never fail.
        await ws.SendJsonAsync(new { id = signInHandlerId }, ct: TestContext.Current.CancellationToken);
        var second = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));

        Assert.Null(second);
        Assert.Equal(0, session.PendingHandlers);
    }

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<SignInTestApp>(
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
                // Capture the Set-Cookie issued by /_rask/auth/redeem so we can replay it on
                // the WS upgrade in the reconnect test (TestServer doesn't bridge cookies
                // between HttpClient and WebSocketClient).
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
        // Match <button ... data-rask-on-click="hN">...buttonText...</button>
        var pattern = "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" +
                      "[^<]*" + Regex.Escape(buttonText);
        var match = Regex.Match(html, pattern);
        Assert.True(match.Success, $"button with text '{buttonText}' not found in html");
        return match.Groups[1].Value;
    }
}

internal sealed class TestCookieJar
{
    public string? Cookie;
}
