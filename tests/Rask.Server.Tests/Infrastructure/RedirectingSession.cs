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
internal sealed class RedirectingSession(RaskTestHost host, WebSocket ws, string html, string sessionId) : IAsyncDisposable
{
    public WebSocket Ws => ws;

    public RaskTestHost Server => host;

    /// <summary>The page as the first request was answered.</summary>
    public string FirstHtml => html;

    /// <summary>The server's side of this connection: its services, and the route it is on.</summary>
    public LiveSession Session => host.Store.Get(sessionId)!;

    /// <param name="diffMode">The wire shape the host's sessions render with.</param>
    /// <param name="quiescence">How long a first request waits for a page's load; the host's default when null.</param>
    public static RaskTestHost Host(LiveDiffMode diffMode, TimeSpan? quiescence = null) =>
        RaskTestHost.Create<RedirectingApp>(
            services =>
            {
                services.AddScoped<RedirectChoice>();
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
            configureServer: quiescence is { } budget ? o => o.QuiescenceTimeout = budget : null,
            diffMode: diffMode);

    /// <param name="path">The page the first request asks for.</param>
    /// <param name="diffMode">The wire shape the session renders with.</param>
    /// <param name="signedInAs">The name the reader is signed in under; anonymous when null.</param>
    /// <param name="quiescence">How long the first request waits for the page's load.</param>
    /// <param name="hello">False leaves the socket open but unattached, for a test that says hello itself.</param>
    public static async Task<RedirectingSession> Open(
        string path, LiveDiffMode diffMode = LiveDiffMode.Auto, string? signedInAs = null,
        TimeSpan? quiescence = null, bool hello = true)
    {
        var host = Host(diffMode, quiescence);
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
        var session = new RedirectingSession(host, ws, html, sessionId);
        if (hello)
        {
            await session.Hello();
            await ws.AttachedAsync(host, sessionId);
        }

        return session;
    }

    public Task Hello() => ws.SendJsonAsync(new { type = "hello", session = sessionId });

    public Task Send(object frame) => ws.SendJsonAsync(frame);

    // Every frame the navigation produced: the server has finished with it when these come back.
    public async Task<List<string>> Navigate(string path, bool replace = false)
    {
        await ws.SendJsonAsync(new { type = "navigate", path, query = "", replace });
        return await ws.SettledAsync();
    }

    public async Task<List<string>> Click(string buttonId)
    {
        await Press(buttonId);
        return await ws.SettledAsync();
    }

    /// <summary>Clicks without waiting for what the click leads to.</summary>
    public Task Press(string buttonId)
    {
        var handler = Regex.Match(
            html, $"id=\"{Regex.Escape(buttonId)}\"[^>]*data-rask-on-click=\"([^\"]+)\"",
            RegexOptions.None, TimeSpan.FromSeconds(1));
        return ws.SendJsonAsync(new { id = handler.Groups[1].Value, type = "click" });
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
