using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A click whose render the diff gate refuses is answered with the whole page. In Development the session says
// which node it gave up on, on Rask.Live; in production it says nothing and builds no message.
[Collection("HostEnvironment")]
public sealed class FullPageReplyReasonTests
{
    [Fact]
    public async Task In_development_a_reply_that_falls_back_to_the_whole_page_names_the_node_that_changed_shape()
    {
        using var host = RaskTestHost.Create<ShapeSwitchApp>(environment: "Development");
        var said = await SwapAsync(host);

        var reason = Assert.Single(said.Reasons);

        Assert.Contains("\"html\":\"<!DOCTYPE", said.Frame, StringComparison.Ordinal);
        Assert.Equal(RaskLogLevel.Information, reason.Level);
        Assert.Contains("went out as the whole page", reason.Message, StringComparison.Ordinal);
        Assert.Contains("is by position, replaced by <div id=\"picked\">Glock</div>", reason.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_production_the_same_fallback_says_nothing()
    {
        using var host = RaskTestHost.Create<ShapeSwitchApp>(environment: "Production");
        var said = await SwapAsync(host);

        var reasons = said.Reasons;

        Assert.Contains("\"html\":\"<!DOCTYPE", said.Frame, StringComparison.Ordinal);
        Assert.Empty(reasons);
    }

    private static async Task<(string Frame, List<RaskDiagnosticEvent> Reasons)> SwapAsync(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId, TimeSpan.FromSeconds(5));
        var handler = Regex.Match(html, "id=\"swap\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;

        // After the host is up: mapping it points the sink at its own logger.
        var captured = new ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Enqueue;
        try
        {
            await ws.SendJsonAsync(new { id = handler }, ct: TestContext.Current.CancellationToken);
            var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(frame);

            return (frame, [.. captured.Where(e => string.Equals(e.Category, "Rask.Live", StringComparison.Ordinal))]);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }
}

/// <summary>One child that is a span until the button is pressed and a div after, with a sibling behind it.</summary>
public sealed partial class ShapeSwitchApp : Component
{
    private bool _picked;

    protected override Component? HeadAssets => Title["shape-switch"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        Main[
            _picked ? Div.Id("picked")["Glock"] : Span.Id("placeholder")["Choose…"],
            Button.Id("swap").OnClick(() => _picked = true)["swap"]
        ];
}
