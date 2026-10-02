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
    public async Task An_observer_is_made_with_a_handler_that_gets_its_entries_as_data()
    {
        var browser = new FakeBrowser();
        var widget = new Widget();
        var card = ElementRef.New();

        using (browser.Enter())
        {
            var observer = await widget.Observe();
            await observer.Observe(card);
        }

        await Fire(browser.Calls[0].Args[2], """[[{"isIntersecting":true,"intersectionRatio":0.5},{"isIntersecting":false,"intersectionRatio":0}],{}]""");

        Assert.Equal("""[["n","IntersectionObserver",[{"__raskArg__":0}]]]""", browser.Steps(0));
        Assert.Equal(("""[["c","observe",[{"__raskArg__":0}]]]""", card), (browser.Steps(1), browser.Calls[1].Args[2]));
        Assert.Equal([0.5], widget.Visible);
    }

    [Fact]
    public async Task A_disposed_observer_no_longer_reaches_its_handler()
    {
        var browser = new FakeBrowser();
        var widget = new Widget();
        using (browser.Enter())
        {
            await using var observer = await widget.Observe();
        }

        await Fire(browser.Calls[0].Args[2], """[[{"isIntersecting":true,"intersectionRatio":1}]]""");

        Assert.Empty(widget.Visible);
        Assert.True(browser.Kept[0].Disposed);
    }

    [Fact]
    public async Task A_lock_is_held_until_the_handler_it_runs_has_finished()
    {
        var browser = new FakeBrowser();
        var widget = new Widget();
        using (browser.Enter())
        {
            await widget.Sync();
        }

        var held = Fire(browser.Calls[0].Args[2], """[{"name":"sync","mode":"exclusive"}]""");
        var heldWhileSyncing = !held.IsCompleted;
        widget.Synced.SetResult();
        await held;

        Assert.Equal("""[["g","navigator"],["g","locks"],["c","request",["sync",{"__raskArg__":0}]]]""", browser.Steps(0));
        Assert.Equal((true, "sync", LockMode.Exclusive), (heldWhileSyncing, widget.LockName, widget.Mode));
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

    [Fact]
    public async Task A_live_field_of_an_event_is_kept_for_the_handler_and_let_go_when_its_component_unmounts()
    {
        var browser = new HostedBrowser();
        var widget = new Widget();
        using (FakeBrowser.Enter(browser))
        {
            await widget.WatchUsb();
        }

        var listen = JsonDocument.Parse(browser.Calls[0].ArgsJson!).RootElement;
        await ScopedScript.Invoke(listen[4].GetProperty("__raskCb__").GetInt32(), JsonDocument.Parse("""[{"device":{"__jsObjectId":5}}]""").RootElement);
        widget.CancelLifetimeToken();

        Assert.Equal("""["*device","type","eventPhase","bubbles","cancelable","defaultPrevented","composed","isTrusted","timeStamp"]""", listen[3].GetString());
        Assert.NotNull(widget.Left);
        Assert.EndsWith("disposeJSObjectReferenceById", browser.Calls[^1].Identifier, StringComparison.Ordinal);
        Assert.Equal("[5]", browser.Calls[^1].ArgsJson);
    }

    [Fact]
    public async Task An_install_prompt_event_is_kept_whole_so_it_can_be_prompted_later()
    {
        var browser = new HostedBrowser();
        var widget = new Widget();
        using (FakeBrowser.Enter(browser))
        {
            await widget.WatchInstall();
        }

        var listen = JsonDocument.Parse(browser.Calls[0].ArgsJson!).RootElement;
        await ScopedScript.Invoke(listen[4].GetProperty("__raskCb__").GetInt32(), JsonDocument.Parse("""[{"":{"__jsObjectId":7}}]""").RootElement);
        browser.Answer = """{"userChoice":"accepted"}""";
        var choice = await widget.Deferred!.Prompt();

        var prompt = JsonDocument.Parse(browser.Calls[^1].ArgsJson!).RootElement;
        Assert.StartsWith("""["*","type",""", listen[3].GetString(), StringComparison.Ordinal);
        Assert.Equal(AppBannerPromptOutcome.Accepted, choice.UserChoice);
        Assert.Equal((7, """[["c","prompt"]]"""), (prompt[0].GetProperty("__jsObjectId").GetInt32(), prompt[1].GetString()));
    }

    // What the browser does when it fires: calls the function it was handed, with the listener's payload.
    private static Task Fire(object? callback, string args) =>
        ScopedScript.Invoke(((ScopedScript.ScriptCallback)callback!).Id, JsonDocument.Parse(args).RootElement);

    private sealed class Widget : Component
    {
        public USBDevice? Left { get; private set; }

        public ValueTask<IAsyncDisposable> WatchUsb() => Navigator.Usb.OnDisconnect(e => Left = e.Device);

        public BeforeInstallPromptEvent? Deferred { get; private set; }

        public ValueTask<IAsyncDisposable> WatchInstall() => Window.OnBeforeInstallPrompt(e => Deferred = e);

        public bool Wide { get; private set; }

        public string Query { get; private set; } = "";

        public double Latitude { get; private set; }

        public ValueTask<IAsyncDisposable> WatchWidth() =>
            Window.MatchMedia("(min-width: 800px)").OnChange(e =>
            {
                Wide = e.Matches;
                Query = e.Media;
            });

        public double[] Visible { get; private set; } = [];

        public string? LockName { get; private set; }

        public LockMode Mode { get; private set; }

        public TaskCompletionSource Synced { get; } = new();

        public ValueTask Locate() => Navigator.Geolocation.GetCurrentPosition(p => Latitude = p.Coords.Latitude);

        public ValueTask<Types.IntersectionObserver> Observe() =>
            IntersectionObserver.Create(entries => Visible = entries.Where(e => e.IsIntersecting).Select(e => e.IntersectionRatio).ToArray());

        public ValueTask Sync() =>
            Navigator.Locks.Request("sync", async lk =>
            {
                (LockName, Mode) = (lk?.Name, lk?.Mode ?? default);
                await Synced.Task;
            });
    }
}
