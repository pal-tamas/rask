using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Two hosts in one process, one in Development and one in Production, both running: each shows what its own
// environment shows, whichever started first. Whether a visitor reads an exception's text is the host's to
// decide, and a process-wide flag claimed by one host once decided it for the other.
[Collection("HostEnvironment")]
public sealed class HostModeIsPerHostTests
{
    private const string SaveFailed = "The save failed. Please try again.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_save_toast_names_the_exception_on_the_development_host_alone(bool developmentFirst)
    {
        var (development, production) = Both<SaveFaultApp>(developmentFirst);
        using var _ = development;
        using var __ = production;

        var shown = await Saved(development);
        var hidden = await Saved(production);

        Assert.Contains($"{SaveFailed} InvalidOperationException: Login failed", shown, StringComparison.Ordinal);
        Assert.Contains(SaveFailed, hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", hidden, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_handler_fault_carries_the_overlay_on_the_development_host_alone(bool developmentFirst)
    {
        var (development, production) = Both<ThrowingApp>(developmentFirst, LiveDiffMode.DisabledFull);
        using var _ = development;
        using var __ = production;

        var shown = await Clicked(development);
        var hidden = await Clicked(production);

        Assert.Contains("\"devError\"", shown, StringComparison.Ordinal);
        Assert.Contains("boom", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("devError", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("boom", hidden, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_error_page_names_the_exception_on_the_development_host_alone(bool developmentFirst)
    {
        var (development, production) = Both<ThrowingOnRenderApp>(developmentFirst, LiveDiffMode.DisabledFull);
        using var _ = development;
        using var __ = production;

        var shown = await FirstResponse(development);
        var hidden = await FirstResponse(production);

        Assert.Contains("render-boom", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("render-boom", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", hidden, StringComparison.Ordinal);
    }

    // Both hosts started, in the order asked, and both kept running while the test reads them.
    private static (RaskTestHost Development, RaskTestHost Production) Both<TApp>(
        bool developmentFirst, LiveDiffMode diffMode = LiveDiffMode.Auto)
        where TApp : Rask.Core.Component
    {
        if (developmentFirst)
        {
            var development = RaskTestHost.Create<TApp>(diffMode: diffMode, environment: "Development");
            return (development, RaskTestHost.Create<TApp>(diffMode: diffMode, environment: "Production"));
        }

        var production = RaskTestHost.Create<TApp>(diffMode: diffMode, environment: "Production");
        return (RaskTestHost.Create<TApp>(diffMode: diffMode, environment: "Development"), production);
    }

    private static async Task<string> FirstResponse(RaskTestHost host)
    {
        var response = await host.Http.GetAsync("/start", TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<(WebSocket Socket, string Html)> Connected(RaskTestHost host, string path)
    {
        var html = await host.Http.GetStringAsync(path, TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId);
        return (ws, html);
    }

    // The save pressed with "other" typed: that exception names a password in its message.
    private static async Task<string> Saved(RaskTestHost host)
    {
        var (ws, html) = await Connected(host, "/");
        using var _ = ws;
        var submit = new { id = MarkupAssert.RequireAttr(html, "data-rask-on-submit"), type = "submit", form = new { Name = "other" } };
        await ws.SendTypedThenAsync(MarkupAssert.RequireAttr(html, "data-rask-on-change"), "other", submit);
        var frames = await ws.SettledAsync();
        return string.Join('\n', frames.Select(static f => System.Net.WebUtility.HtmlDecode(Regex.Unescape(f))));
    }

    // The throwing button pressed, and the payload that answered it.
    private static async Task<string> Clicked(RaskTestHost host)
    {
        var (ws, html) = await Connected(host, "/start");
        using var _ = ws;
        var throwing = Regex.Match(html, "data-rask-on-click=\"(h\\d+)\"").Groups[1].Value;
        await ws.SendJsonAsync(new { id = throwing });
        return await ws.ReceiveTextAsync();
    }
}
