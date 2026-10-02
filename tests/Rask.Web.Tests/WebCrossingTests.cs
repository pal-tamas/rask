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
    public async Task An_any_argument_is_the_apps_own_value_written_beside_the_steps_for_the_host_to_revive()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await using var channel = await BroadcastChannel.Create("cart");
            await channel.PostMessage(new CartChanged(42));
        }

        Assert.Equal("""[["c","postMessage",[{"__raskAny__":0}]]]""", browser.Steps(1));
        Assert.Equal("""{"id":42}""", Written(browser.Calls[1].Args[2]));
    }

    [Fact]
    public async Task Bytes_handed_as_an_any_cross_marked_for_the_browser_alone_or_inside_your_own_type()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await using var stream = await WritableStream.Create();
            await using var writer = await stream.GetWriter();
            await writer.Write(new byte[] { 1, 2, 3 });
            await writer.Write(new Chunk([4, 5]));
        }

        Assert.Equal("""[["c","write",[{"__raskAny__":0}]]]""", browser.Steps(2));
        Assert.Equal("""{"__raskBytes__":"AQID"}""", Written(browser.Calls[2].Args[2]));
        Assert.Equal("""{"data":{"__raskBytes__":"BAU="}}""", Written(browser.Calls[3].Args[2]));
    }

    [Fact]
    public async Task A_stream_read_answers_with_your_own_type_and_its_bytes_as_a_byte_array()
    {
        var browser = new FakeBrowser().Answers("""{"value":"AQID","done":false}""");

        ReadableStreamReadResult<byte[]> chunk;
        using (browser.Enter())
        {
            await using var stream = await ReadableStream.Create();
            await using var reader = await stream.GetReader();
            chunk = await reader.Read<byte[]>();
            await reader.ReleaseLock();
        }

        Assert.Equal(("__raskWeb.read", """[["c","read"]]"""), (browser.Calls[2].Identifier, browser.Steps(2)));
        Assert.False(chunk.Done);
        Assert.Equal(new byte[] { 1, 2, 3 }, chunk.Value);
        Assert.Equal("""[["c","releaseLock"]]""", browser.Steps(3));
    }

    [Fact]
    public async Task A_string_or_a_number_crosses_as_the_number_it_spells_or_as_the_name_it_is()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await using var device = await Navigator.Bluetooth.RequestDevice(new()
            {
                Filters = [new() { Services = ["battery_service", "6159"] }],
                OptionalServices = ["0x180F"],
            });
        }

        Assert.Equal(
            """[["g","navigator"],["g","bluetooth"],["c","requestDevice",[{"filters":[{"services":["battery_service",6159]}],"optionalServices":["0x180F"]}]]]""",
            browser.Steps(0));
    }

    [Fact]
    public async Task A_map_of_a_value_or_a_list_of_it_takes_the_list()
    {
        var browser = new FakeBrowser().Answers("[]");

        using (browser.Enter())
        {
            await Window.ShowOpenFilePicker(new() { Types = [new() { Description = "Text", Accept = new() { ["text/plain"] = [".txt"] } }] });
        }

        Assert.Equal(
            """[["c","showOpenFilePicker",[{"types":[{"description":"Text","accept":{"text/plain":[".txt"]}}]}]]]""",
            browser.Steps(0));
    }

    [Fact]
    public async Task A_value_or_a_list_of_it_is_written_as_the_list_and_read_back_from_a_lone_value()
    {
        var browser = new FakeBrowser().Answers("""{"iceServers":[{"urls":"stun:x"},{"urls":["stun:y","stun:z"]}]}""");

        RTCConfiguration configuration;
        using (browser.Enter())
        {
            await using var connection = await RTCPeerConnection.Create();
            await connection.SetConfiguration(new() { IceServers = [new() { Urls = ["stun:x"] }] });
            configuration = await connection.GetConfiguration();
        }

        Assert.Equal("""[["c","setConfiguration",[{"iceServers":[{"urls":["stun:x"]}]}]]]""", browser.Steps(1));
        Assert.Equal(new[] { "stun:x" }, configuration.IceServers![0].Urls);
        Assert.Equal(new[] { "stun:y", "stun:z" }, configuration.IceServers[1].Urls);
    }

    [Fact]
    public async Task A_boolean_or_dictionary_is_written_as_the_dictionary_and_left_out_when_null()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await using var stream = await Navigator.MediaDevices.GetUserMedia(new() { Video = new() });
        }

        Assert.Equal("""[["g","navigator"],["g","mediaDevices"],["c","getUserMedia",[{"video":{}}]]]""", browser.Steps(0));
    }

    [Fact]
    public void A_boolean_or_dictionary_answered_as_a_boolean_reads_true_as_empty_and_false_as_null()
    {
        const string answer = """{"video":true,"audio":false}""";

        var constraints = JsonSerializer.Deserialize<MediaStreamConstraints>(answer)!;

        Assert.Equal((new MediaTrackConstraints(), (MediaTrackConstraints?)null), (constraints.Video, constraints.Audio));
    }

    [Fact]
    public async Task A_media_constraint_is_written_as_its_plain_value()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await using var stream = await Navigator.MediaDevices.GetUserMedia(new() { Video = new() { Width = 640, FacingMode = "user", Torch = true } });
        }

        Assert.Equal("""[["g","navigator"],["g","mediaDevices"],["c","getUserMedia",[{"video":{"torch":true,"width":640,"facingMode":"user"}}]]]""",
            browser.Steps(0));
    }

    [Fact]
    public async Task A_media_constraint_read_back_as_an_object_is_its_ideal_else_its_exact_else_null()
    {
        var browser = new FakeBrowser().Answers(
            "[true]", """{"width":{"ideal":640,"max":1920},"height":{"exact":480},"frameRate":{"min":24},"facingMode":["user","environment"],"torch":true}""");

        MediaTrackConstraints constraints;
        using (browser.Enter())
        {
            await using var stream = await Navigator.MediaDevices.GetUserMedia(new() { Video = new() });
            var tracks = await stream.GetTracks();
            constraints = await tracks[0].GetConstraints();
        }

        Assert.Equal(((int?)640, (int?)480, (double?)null, (string?)"user", (bool?)true),
            (constraints.Width, constraints.Height, constraints.FrameRate, constraints.FacingMode, constraints.Torch));
    }

    [Fact]
    public async Task A_list_of_objects_that_are_only_values_is_read_whole_as_records()
    {
        var browser = new FakeBrowser().Answers("[true]", """[{"pressed":true,"touched":true,"value":1}]""");

        GamepadButton[] buttons;
        using (browser.Enter())
        {
            var pads = await Navigator.GetGamepads();
            buttons = await pads[0]!.Buttons;
        }

        Assert.Equal(("__raskWeb.read", """[["g","buttons"]]"""), (browser.Calls[3].Identifier, browser.Steps(3)));
        Assert.Equal(new GamepadButton { Pressed = true, Touched = true, Value = 1 }, buttons.Single());
    }

    // What the host's runtime is handed to write: the app's value, already written with the host's options.
    private static string Written(object? extra) => ((JsonElement)extra!).GetRawText();

    public sealed record Chunk(byte[] Data);

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
