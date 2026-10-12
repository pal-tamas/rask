using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A click whose render the diff gate refuses is answered with the whole page. In Development the session says
// which node it gave up on, on Rask.Live; in production it says nothing and builds no message. A child that
// changes element is refused only as a child of the body; inside an element of the page that element's children
// are morphed alone, and Development says that too, as information.
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
        Assert.Equal(RaskLogLevel.Warning, reason.Level);
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

    [Fact]
    public async Task A_development_host_mapped_through_endpoint_routing_under_a_path_base_names_the_node_too()
    {
        using var host = RaskTestHost.Create<ShapeSwitchApp>(environment: "Development", pathBase: "/uj", endpointRouting: true);
        var said = await SwapAsync(host, "/uj/");

        var reason = Assert.Single(said.Reasons);

        Assert.Contains("\"html\":\"<!DOCTYPE", said.Frame, StringComparison.Ordinal);
        Assert.Equal(RaskLogLevel.Warning, reason.Level);
        Assert.Contains("is by position, replaced by <div id=\"picked\">Glock</div>", reason.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_production_host_mapped_through_endpoint_routing_says_nothing()
    {
        using var host = RaskTestHost.Create<ShapeSwitchApp>(environment: "Production", pathBase: "/uj", endpointRouting: true);
        var said = await SwapAsync(host, "/uj/");

        var reasons = said.Reasons;

        Assert.Contains("\"html\":\"<!DOCTYPE", said.Frame, StringComparison.Ordinal);
        Assert.Empty(reasons);
    }

    [Fact]
    public async Task In_development_a_child_that_changes_element_inside_the_page_is_a_diff_that_names_the_parent_morphed_for_it()
    {
        using var host = RaskTestHost.Create<NestedShapeSwitchApp>(environment: "Development");
        var said = await SwapAsync(host);

        var morphed = Assert.Single(said.Reasons);

        Assert.StartsWith("{\"kind\":\"diff\",\"ops\":[[8,[1,1,0],\"<div id=\\\"picked\\\">Glock</div><button", said.Frame, StringComparison.Ordinal);
        Assert.Equal(RaskLogLevel.Information, morphed.Level);
        Assert.Contains("the children of /1/1/0 went out as one subtree", morphed.Message, StringComparison.Ordinal);
        Assert.Contains("RemoveSubtree at /1/1/0/0 is by position, replaced by <div id=\"picked\">Glock</div>", morphed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("whole page", morphed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_production_the_same_morph_says_nothing()
    {
        using var host = RaskTestHost.Create<NestedShapeSwitchApp>(environment: "Production");
        var said = await SwapAsync(host);

        var reasons = said.Reasons;

        Assert.StartsWith("{\"kind\":\"diff\",\"ops\":[[8,[1,1,0],", said.Frame, StringComparison.Ordinal);
        Assert.Empty(reasons);
    }

    [Fact]
    public async Task A_press_that_only_calls_script_is_answered_with_a_diff_that_carries_the_call()
    {
        using var host = RaskTestHost.Create<ScriptOnlyApp>(environment: "Development");
        var said = await SwapAsync(host);

        var frame = said.Frame;

        Assert.StartsWith("{\"kind\":\"diff\",\"ops\":[]", frame, StringComparison.Ordinal);
        Assert.Contains("\"identifier\":\"test.noop\"", frame, StringComparison.Ordinal);
        Assert.Empty(said.Reasons);
    }

    private static async Task<(string Frame, List<RaskDiagnosticEvent> Reasons)> SwapAsync(RaskTestHost host, string path = "/")
    {
        var html = await host.Http.GetStringAsync(path, TestContext.Current.CancellationToken);
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
            var frame = await ws.ReceiveUntilAsync(static frame => !frame.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal), "the reply to the press");

            return (frame, [.. captured.Where(e => string.Equals(e.Category, "Rask.Live", StringComparison.Ordinal))]);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }
}

/// <summary>
///     One child of the body that is a span until the button is pressed and a div after, with a sibling behind
///     it: there is no element of the page around it to send instead.
/// </summary>
public sealed partial class ShapeSwitchApp : Component
{
    private bool _picked;

    protected override Component? HeadAssets => Title["shape-switch"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        _picked ? Div.Id("picked")["Glock"] : Span.Id("placeholder")["Choose…"],
        Button.Id("swap").OnClick(() => _picked = true)["swap"],
    ];
}

/// <summary>The same child inside a <c>main</c>, which is what travels when it changes element.</summary>
public sealed partial class NestedShapeSwitchApp : Component
{
    private bool _picked;

    protected override Component? HeadAssets => Title["nested-shape-switch"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        Main[
            _picked ? Div.Id("picked")["Glock"] : Span.Id("placeholder")["Choose…"],
            Button.Id("swap").OnClick(() => _picked = true)["swap"]
        ];
}

/// <summary>A button whose press calls script and changes nothing on the page.</summary>
public sealed partial class ScriptOnlyApp(IJSRuntime js) : Component
{
    protected override Component? HeadAssets => Title["script-only"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        Main[Button.Id("swap").OnClick(() => { _ = js.InvokeVoidAsync("test.noop"); })["call"]];
}
