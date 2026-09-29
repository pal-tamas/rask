using System.Text.Json;
using Rask.Core;
using Rask.Core.ScopedAssets;
using Rask.Web.Types;

namespace Rask.Web.Tests;

// An object's events, subscribed to from a component: the browser calls back with the event's fields, the handler runs
// in its component, and disposing of the subscription removes the listener. Callbacks an API takes ride the same way.
public sealed class WebEventTests
{
    [Fact]
    public async Task A_subscription_listens_for_MDNs_event_and_hands_the_handler_MDNs_payload()
    {
        var browser = new FakeBrowser().Answers("7");
        var widget = new Widget();

        IAsyncDisposable subscription;
        using (browser.Enter())
        {
            subscription = await widget.WatchWidth();
        }

        await Fire(browser.Calls[0].Args[4], """[{"type":"change","media":"(min-width: 800px)","matches":true}]""");

        Assert.Equal(("__raskWeb.listen", "change", """["media","matches","type","eventPhase","bubbles","cancelable","defaultPrevented","composed","isTrusted","timeStamp"]"""),
            (browser.Calls[0].Identifier, (string)browser.Calls[0].Args[2]!, (string)browser.Calls[0].Args[3]!));
        Assert.Equal((true, "(min-width: 800px)"), (widget.Wide, widget.Query));
        await subscription.DisposeAsync();
        Assert.Equal(("__raskWeb.unlisten", 7), (browser.Calls[1].Identifier, (int)browser.Calls[1].Args[0]!));
    }

    [Fact]
    public async Task A_disposed_subscription_no_longer_reaches_its_handler()
    {
        var browser = new FakeBrowser().Answers("1");
        var widget = new Widget();
        IAsyncDisposable subscription;
        using (browser.Enter())
        {
            subscription = await widget.WatchWidth();
        }

        await subscription.DisposeAsync();
        await Fire(browser.Calls[0].Args[4], """[{"matches":true}]""");

        Assert.False(widget.Wide);
    }

    [Fact]
    public async Task A_callback_argument_is_the_components_handler_handed_to_the_browser()
    {
        var browser = new FakeBrowser();
        var widget = new Widget();

        using (browser.Enter())
        {
            await widget.Locate();
        }

        await Fire(browser.Calls[0].Args[2], """[{"coords":{"latitude":47.5,"longitude":19.04,"accuracy":10},"timestamp":1}]""");

        Assert.Equal("""[["g","navigator"],["g","geolocation"],["c","getCurrentPosition",[{"__raskArg__":0}]]]""", browser.Steps(0));
        Assert.Equal(47.5, widget.Latitude);
    }

    [Fact]
    public async Task A_handler_that_belongs_to_no_component_is_refused()
    {
        var browser = new FakeBrowser();

        Task Subscribe()
        {
            using (browser.Enter())
            {
                return Window.OnResize(static () => { }).AsTask();
            }
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(Subscribe);

        Assert.Contains("has to belong to a component", error.Message, StringComparison.Ordinal);
    }

    // What the browser does when it fires: calls the function it was handed, with the listener's payload.
    private static Task Fire(object? callback, string args) =>
        ScopedScript.Invoke(((ScopedScript.ScriptCallback)callback!).Id, JsonDocument.Parse(args).RootElement);

    private sealed class Widget : Component
    {
        public bool Wide { get; private set; }

        public string Query { get; private set; } = "";

        public double Latitude { get; private set; }

        public ValueTask<IAsyncDisposable> WatchWidth() =>
            Window.MatchMedia("(min-width: 800px)").OnChange(e =>
            {
                Wide = e.Matches;
                Query = e.Media;
            });

        public ValueTask Locate() => Navigator.Geolocation.GetCurrentPosition(p => Latitude = p.Coords!.Latitude ?? 0);
    }
}
