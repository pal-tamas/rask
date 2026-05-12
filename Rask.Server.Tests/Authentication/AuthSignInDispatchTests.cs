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
    public async Task SignInHandler_EmitsAuthBlockAndHistoryReplace()
    {
        using var host = CreateHost();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);
        var signInHandlerId = ExtractHandlerId(initialHtml, "sign-in");

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = signInHandlerId });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.True(doc.RootElement.TryGetProperty("auth", out var authEl));
        Assert.Equal(JsonValueKind.String, authEl.GetProperty("ticket").ValueKind);
        Assert.Equal("/dashboard", authEl.GetProperty("returnUrl").GetString());

        Assert.True(doc.RootElement.TryGetProperty("history", out var histEl));
        Assert.Equal("replace", histEl.GetProperty("action").GetString());
        Assert.Equal("/dashboard", histEl.GetProperty("url").GetString());
    }

    [Fact]
    public async Task RedeemThenReconnect_AppliesNewIdentity()
    {
        using var host = CreateHost();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);
        var signInHandlerId = ExtractHandlerId(initialHtml, "sign-in");

        // Initial state: anonymous
        Assert.Contains("user=anon", initialHtml);

        using (var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None))
        {
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

            await ws.SendJsonAsync(new { id = signInHandlerId });
            var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(text);

            using var doc = JsonDocument.Parse(text!);
            var ticket = doc.RootElement.GetProperty("auth").GetProperty("ticket").GetString();

            // Simulate the JS fetch hitting /_rask/auth/redeem; a real browser shares
            // cookies between fetch and the next WS upgrade — here we use a CookieContainer-
            // backed HttpClient to do the same.
            var redeem = await host.Http.PostAsJsonAsync(
                "/_rask/auth/redeem",
                new { ticket, session = sessionId });
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
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId });
        var afterReconnect = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(afterReconnect);

        using var doc2 = JsonDocument.Parse(afterReconnect!);
        var html = doc2.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("user=alice", html);
        Assert.Contains("path=/dashboard", html);
    }

    [Fact]
    public async Task SuppressEvents_DropsClicksAfterAuthEmit()
    {
        using var host = CreateHost();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);
        var signInHandlerId = ExtractHandlerId(initialHtml, "sign-in");

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = signInHandlerId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // Now the session is in suppressed mode. A second click should produce no payload.
        await ws.SendJsonAsync(new { id = signInHandlerId });
        var second = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.Null(second);
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

    private static string ExtractSessionId(string html) =>
        Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;

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
