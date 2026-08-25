using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Live;

/// <summary>
///     A resumed session keeps the visitor's language.
/// </summary>
/// <remarks>
///     Resume exists to hide a host restart from the visitor. Rebuilding their page in the wrong language
///     would be a conspicuous way to fail at that — and it is the easy bug to ship, because a resumed
///     session is a <b>brand new DI scope</b>: its culture starts at the app default no matter what the
///     old one held. The signals are still on the WebSocket upgrade request, which is where the culture
///     is negotiated for this path.
/// </remarks>
public sealed class CultureResumeTests
{
    // Declares a value as well as the language: a resume record only rides a payload that actually
    // changed, so the page needs something to change.
    private sealed class LanguagePage(IPersistentState state) : Component
    {
        protected override Component? Render()
        {
            state.TryGet<int>("counter", out var count);
            // A second P rather than Span: the HTML Span entry collides with System.Span<T> here.
            return Div[P[UICulture.Name], P[count.ToString(CultureInfo.InvariantCulture)]];
        }
    }

    private static RaskTestHost Host() =>
        RaskTestHost.Create<LanguagePage>(configureCulture: c =>
        {
            c.SupportedCultures.Add("en");
            c.SupportedCultures.Add("hu");
        });

    [Fact]
    public async Task A_rebuilt_session_is_still_in_the_visitors_language()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c%3Dhu%7Cuic%3Dhu");

        // Establish a Hungarian session and take its resume record off the wire.
        var initial = await host.Http.GetAsync("/");
        var html = await initial.Content.ReadAsStringAsync();
        Assert.Contains("<p>hu</p>", html, StringComparison.Ordinal);

        var sessionId = SessionIdFrom(html);
        host.WebSockets.ConfigureRequest = r => r.Headers["Cookie"] = ".AspNetCore.Culture=c%3Dhu%7Cuic%3Dhu";

        using (var first = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None))
        {
            await first.SendJsonAsync(new { type = "hello", session = sessionId });
            var session = host.Store.Get(sessionId)!;
            session.Services.GetRequiredService<IPersistentState>().Persist("counter", 41);
            await session.View.StateHasChangedAsync();
            var token = await ReadResumeAsync(first);

            // The process that held the tree is gone — a restart, or a deploy swapping the container.
            host.Store.Remove(sessionId);

            using var second = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await second.SendJsonAsync(new { type = "hello", session = sessionId, resume = token });

            var rebuilt = await ReadHtmlAsync(second);

            // Not the app default. The rebuild read the upgrade request's cookie and seeded the new scope.
            Assert.Contains("<p>hu</p>", rebuilt, StringComparison.Ordinal);
            Assert.Contains("lang=\"hu\"", rebuilt, StringComparison.Ordinal);
        }
    }

    private static string SessionIdFrom(string html)
    {
        const string marker = "data-rask-root=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "the shell did not carry a session id");
        start += marker.Length;
        return html[start..html.IndexOf('"', start)];
    }

    private static async Task<string> ReadResumeAsync(WebSocket ws)
    {
        for (var i = 0; i < 8; i++)
        {
            var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            if (frame is null)
            {
                break;
            }

            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("resume", out var resume)
                && resume.ValueKind == JsonValueKind.String)
            {
                return resume.GetString()!;
            }
        }

        Assert.Fail("expected a render payload carrying a resume record");
        return string.Empty;
    }

    private static async Task<string> ReadHtmlAsync(WebSocket ws)
    {
        for (var i = 0; i < 8; i++)
        {
            var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            if (frame is null)
            {
                break;
            }

            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("html", out var html) && html.ValueKind == JsonValueKind.String)
            {
                return html.GetString()!;
            }
        }

        Assert.Fail("expected a rebuild frame carrying html");
        return string.Empty;
    }
}
