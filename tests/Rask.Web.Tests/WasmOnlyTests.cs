using System.Reflection;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Dom.Build;
using Rask.Web.Types;

namespace Rask.Web.Tests;

// What only WebAssembly can run is generated into Rask.Wasm, as extensions of Rask.Web's own types: a server app, which
// references Rask.Web alone, cannot call it at all, and a WebAssembly app calls it as though it were any other member.
public sealed class WasmOnlyTests
{
    private static readonly Assembly Web = typeof(Window).Assembly;
    private static readonly Assembly Wasm = typeof(WasmMembers).Assembly;

    [Fact]
    public void Every_call_that_needs_the_users_click_is_in_the_snapshot_and_never_on_the_server()
    {
        var interfaces = Snapshot().GetProperty("interfaces");

        var misplaced = new List<string>();
        foreach (var gated in WebHost.Activation)
        {
            var (iface, idl) = (gated.Split('.')[0], gated.Split('.')[1]);
            var inSnapshot = interfaces.TryGetProperty(iface, out var i) && Members(i).Contains(idl);
            var method = Pascal(idl);
            var onServer = Declares(Web.GetType("Rask.Web.Types." + iface), method) || Declares(Web.GetType("Rask.Web." + iface), method)
                           || OnRef(typeof(ElementRefMembers), iface, method);
            if (!inSnapshot || onServer)
            {
                misplaced.Add(gated);
            }
        }

        Assert.Empty(misplaced);
    }

    // The list also holds calls nothing generates yet, because what they hand back is a live object (window.open's
    // WindowProxy, a video's PictureInPictureWindow): the day one is, it lands in WebAssembly. These are generated.
    [Theory]
    [InlineData("Navigator", "share")]
    [InlineData("PaymentRequest", "show")]
    [InlineData("MediaDevices", "getDisplayMedia")]
    [InlineData("Notification", "requestPermission")]
    [InlineData("Element", "requestFullscreen")]
    [InlineData("HTMLInputElement", "showPicker")]
    [InlineData("USB", "requestDevice")]
    [InlineData("IdleDetector", "requestPermission")]
    public void A_generated_call_that_needs_the_click_is_in_WebAssembly(string iface, string idl)
    {
        var method = Pascal(idl);

        // Instance members in WasmMembers; a class's statics in its own {Class}WasmMembers.
        var inWasm = Declares(typeof(WasmMembers), method) || Declares(Wasm.GetType($"Rask.Web.{iface}WasmMembers"), method)
                     || OnRef(typeof(WasmElementRefMembers), iface, method);

        Assert.True(inWasm, $"{iface}.{idl}");
        Assert.Contains($"{iface}.{idl}", WebHost.Activation);
    }

    [Fact]
    public void A_family_driven_every_frame_is_WebAssemblys_whole()
    {
        var server = Web.GetType("Rask.Web.Types.GPUDevice");
        var browser = Wasm.GetType("Rask.Web.Types.GPUDevice");

        Assert.Null(server);
        Assert.NotNull(browser);
        Assert.Null(typeof(Rask.Web.Types.Navigator).GetProperty("Gpu"));
        Assert.Null(Web.GetType("Rask.Web.Types.OES_vertex_array_object"));
    }

    [Fact]
    public async Task In_WebAssembly_a_call_that_needs_the_click_runs_like_any_other()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await Navigator.Share(new ShareData { Title = "Rask", Url = "https://rask.sh" });
        }

        Assert.Equal("""[["g","navigator"],["c","share",[{"title":"Rask","url":"https://rask.sh"}]]]""", browser.Steps(0));
    }

    [Fact]
    public async Task A_fake_records_a_WebAssembly_only_call_too()
    {
        using var navigator = Navigator.Fake();

        await Navigator.Share(new ShareData { Title = "Rask" });

        var call = Assert.Single(navigator.Calls);
        Assert.Equal(("share", "Rask"), (call.Member, ((ShareData)call.Args[0]!).Title));
    }

    private static string Pascal(string idl) => char.ToUpperInvariant(idl[0]) + idl[1..];

    // An element ref's member, told apart by its receiver: a dialog's Show() is not a PaymentRequest's.
    private static bool OnRef(Type members, string iface, string method) =>
        members.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Any(m =>
            string.Equals(m.Name, method, StringComparison.Ordinal)
            && m.GetParameters().FirstOrDefault()?.ParameterType is { IsGenericType: true } receiver
            && string.Equals(receiver.GetGenericArguments()[0].Name, iface, StringComparison.Ordinal));

    // An extension member compiles to a static method of its name on the class that declares it.
    private static bool Declares(Type? type, string method) =>
        type?.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(m => string.Equals(m.Name, method, StringComparison.Ordinal)) == true;

    private static HashSet<string> Members(JsonElement iface) =>
        new[] { "members", "statics" }
        .SelectMany(list => iface.TryGetProperty(list, out var items) ? items.EnumerateArray().ToList() : [])
        .Select(x => x.GetProperty("name").GetString()!)
        .ToHashSet(StringComparer.Ordinal);

    private static JsonElement Snapshot()
    {
        var dir = AppContext.BaseDirectory;
        while (!System.IO.File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }

        return JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(dir, "src", "Rask.Core", "Dom", "mdn.snapshot.json"))).RootElement;
    }
}
