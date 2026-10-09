using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Rask.Server.Authentication;

namespace Rask.Server.Tests.Infrastructure;

// An open connection to RedirectingApp: a navigation or a click, and every frame the server sent for it.
internal sealed class RedirectingSession(RaskTestHost host, WebSocket ws, string html) : IAsyncDisposable
{
    public WebSocket Ws => ws;

    public static RaskTestHost Host(LiveDiffMode diffMode) =>
        RaskTestHost.Create<RedirectingApp>(
            services =>
            {
                services.AddAuthentication("TestCookie").AddCookie("TestCookie", o =>
                {
                    o.Cookie.Name = "TestCookie";
                    o.LoginPath = "/login";
                });
                services.AddAuthorization();
            },
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            },
            diffMode: diffMode);

    public static async Task<RedirectingSession> Open(
        string path, LiveDiffMode diffMode = LiveDiffMode.Auto, string? signedInAs = null)
    {
        var host = Host(diffMode);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (signedInAs is not null)
        {
            var cookie = await SignIn(host, signedInAs);
            request.Headers.Add("Cookie", cookie);
            host.WebSockets.ConfigureRequest = upgrade => upgrade.Headers["Cookie"] = cookie;
        }

        var html = await (await host.Http.SendAsync(request)).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId);
        return new RedirectingSession(host, ws, html);
    }

    public Task Send(object frame) => ws.SendJsonAsync(frame);

    // Every frame the navigation produced: the server has finished with it when these come back.
    public async Task<List<string>> Navigate(string path, bool replace = false)
    {
        await ws.SendJsonAsync(new { type = "navigate", path, query = "", replace });
        return await ws.SettledAsync();
    }

    public async Task<List<string>> Click(string buttonId)
    {
        var handler = Regex.Match(
            html, $"id=\"{Regex.Escape(buttonId)}\"[^>]*data-rask-on-click=\"([^\"]+)\"",
            RegexOptions.None, TimeSpan.FromSeconds(1));
        await ws.SendJsonAsync(new { id = handler.Groups[1].Value, type = "click" });
        return await ws.SettledAsync();
    }

    private static async Task<string> SignIn(RaskTestHost host, string name)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, name)], "TestCookie"));
        var ticket = host.Server.Services.GetRequiredService<IAuthTicketStore>()
            .Issue(AuthAction.SignIn, principal, "TestCookie", "sess");
        var response = await host.Http.PostAsJsonAsync("/_rask/auth/redeem", new { ticket, session = "sess" });
        return response.Headers.GetValues("Set-Cookie").First().Split(';')[0];
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // The server may already have closed it.
        }

        ws.Dispose();
        host.Dispose();
    }
}
