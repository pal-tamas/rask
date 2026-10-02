using System.Text.Json;
using Rask.Core;
using Rask.Core.ScopedAssets;
using Rask.Web.Types;

namespace Rask.Web.Tests;

// What crosses besides plain values: bytes, the app's own types where MDN says `any`, arrays of live objects, and the
// dictionary an `object` argument really takes.
public sealed class WebCrossingTests
{
    [Fact]
    public async Task Bytes_cross_marked_for_the_browser_and_come_back_as_a_byte_array()
    {
        var browser = new FakeBrowser().Answers("\"CQgH\"");

        byte[] hash;
        using (browser.Enter())
        {
            hash = await Crypto.Subtle.Digest("SHA-256", [1, 2, 3]);
        }

        Assert.Equal("""[["g","crypto"],["g","subtle"],["c","digest",["SHA-256",{"__raskBytes__":"AQID"}]]]""", browser.Steps(0));
        Assert.Equal(new byte[] { 9, 8, 7 }, hash);
    }

    [Fact]
    public async Task A_method_that_fills_the_bytes_it_is_handed_answers_with_them()
    {
        var browser = new FakeBrowser().Answers("\"KioqKg==\"");

        byte[] salt;
        using (browser.Enter())
        {
            salt = await Crypto.GetRandomValues(new byte[4]);
        }

        Assert.Equal("""[["g","crypto"],["c","getRandomValues",[{"__raskBytes__":"AAAAAA=="}]]]""", browser.Steps(0));
        Assert.Equal(new byte[] { 42, 42, 42, 42 }, salt);
    }

    [Fact]
    public async Task Bytes_inside_a_dictionary_cross_marked_too()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await using var subscription = await PushManager.Subscribe(new() { UserVisibleOnly = true, ApplicationServerKey = [4, 5] });
        }

        Assert.Equal("""[["g","pushManager"],["c","subscribe",[{"userVisibleOnly":true,"applicationServerKey":{"__raskBytes__":"BAU="}}]]]""",
            browser.Steps(0));
    }

    [Fact]
    public async Task An_any_argument_is_the_apps_own_value_handed_to_the_host_to_write()
    {
        var browser = new FakeBrowser();
        var message = new CartChanged(42);

        using (browser.Enter())
        {
            await using var channel = await BroadcastChannel.Create("cart");
            await channel.PostMessage(message);
        }

        Assert.Equal("""[["c","postMessage",[{"__raskArg__":0}]]]""", browser.Steps(1));
        Assert.Same(message, browser.Calls[1].Args[2]);
    }

    [Fact]
    public async Task An_any_result_is_read_as_the_callers_own_type()
    {
        var browser = new FakeBrowser().Answers("""{"Id":42}""");

        CartChanged? cart;
        using (browser.Enter())
        {
            await using var response = await Window.Fetch("/api/cart");
            cart = await response.Json<CartChanged>();
        }

        Assert.Equal(("__raskWeb.read", """[["c","json"]]"""), (browser.Calls[1].Identifier, browser.Steps(1)));
        Assert.Equal(42, cart?.Id);
    }

    [Fact]
    public async Task An_events_any_field_is_read_as_the_handlers_own_type()
    {
        var browser = new FakeBrowser().Answers("1");
        var widget = new Widget();

        using (browser.Enter())
        {
            await using var channel = await BroadcastChannel.Create("cart");
            await widget.Listen(channel);
        }

        await Fire(browser.Calls[1].Args[4], """[{"data":{"id":42},"origin":"https://rask.sh"}]""");

        Assert.Contains("\"data\"", (string)browser.Calls[1].Args[3]!, StringComparison.Ordinal);
        Assert.Equal(42, widget.Received?.Id);
    }

    [Fact]
    public async Task A_sequence_of_live_objects_keeps_each_one_and_lets_the_array_go()
    {
        var browser = new FakeBrowser().Answers("[true,true]");

        USBDevice[] devices;
        using (browser.Enter())
        {
            devices = await Navigator.Usb.GetDevices();
            await devices[0].DisposeAsync();
        }

        Assert.Equal("""[["g","navigator"],["g","usb"],["c","getDevices"]]""", browser.Steps(0));
        Assert.Equal(("__raskWeb.slots", (object?)browser.Kept[0]), (browser.Calls[1].Identifier, browser.Calls[1].Args[0]));
        Assert.Equal(new[] { """[["g","0"]]""", """[["g","1"]]""" }, new[] { browser.Steps(2), browser.Steps(3) });
        Assert.Equal(2, devices.Length);
        Assert.Equal(new[] { true, true, false }, browser.Kept.Select(k => k.Disposed));
    }

    [Fact]
    public async Task An_empty_slot_in_a_sequence_stays_null()
    {
        var browser = new FakeBrowser().Answers("[false,true]");

        Gamepad?[] pads;
        using (browser.Enter())
        {
            pads = await Navigator.GetGamepads();
        }

        Assert.Equal("""[["g","1"]]""", browser.Steps(2));
        Assert.Equal((false, true), (pads[0] is not null, pads[1] is not null));
    }

    [Fact]
    public async Task An_object_argument_is_the_dictionary_MDN_documents_for_it()
    {
        var browser = new FakeBrowser().Answers("\"granted\"");

        PermissionState state;
        using (browser.Enter())
        {
            await using var status = await Navigator.Permissions.Query(new() { Name = "geolocation" });
            state = await status.State;
        }

        Assert.Equal("""[["g","navigator"],["g","permissions"],["c","query",[{"name":"geolocation"}]]]""", browser.Steps(0));
        Assert.Equal(PermissionState.Granted, state);
    }

    [Fact]
    public async Task A_writable_object_attribute_is_set_to_an_object_you_kept_or_to_null()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await Navigator.MediaSession.SetMetadata(await MediaMetadata.Create(new() { Title = "Song" }));
            await Navigator.MediaSession.SetMetadata(null);
        }

        Assert.Equal("""[["n","MediaMetadata",[{"title":"Song"}]]]""", browser.Steps(0));
        Assert.Equal("""[["g","navigator"],["g","mediaSession"],["s","metadata",[{"__raskArg__":0}]]]""", browser.Steps(1));
        Assert.Same(browser.Kept.Single(), browser.Calls[1].Args[2]);
        Assert.Equal("""[["g","navigator"],["g","mediaSession"],["s","metadata",[null]]]""", browser.Steps(2));
    }

    // What the browser does when it fires: calls the function it was handed, with the listener's payload.
    private static Task Fire(object? callback, string args) =>
        ScopedScript.Invoke(((ScopedScript.ScriptCallback)callback!).Id, JsonDocument.Parse(args).RootElement);

    public sealed record CartChanged(int Id);

    private sealed class Widget : Component
    {
        public CartChanged? Received { get; private set; }

        public ValueTask<IAsyncDisposable> Listen(Types.BroadcastChannel channel) => channel.OnMessage(e => Received = e.Data<CartChanged>());
    }
}
