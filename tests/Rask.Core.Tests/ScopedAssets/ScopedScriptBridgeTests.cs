namespace Rask.Core.Tests.ScopedAssets;

// The browser half of ScopedScript, on BOTH hosts: a callback marker revives into a function that calls
// back into .NET, and .NET disposing an object handle lets the host drop the object. The bridge is one
// shared module both hosts import; read from the TypeScript source, as JsBundleGateTests does — the
// shipped bundle is minified.
public class ScopedScriptBridgeTests
{
    private static readonly string Bridge = Path.Combine("src", "Rask.Core", "Resources", "rask-js-invoke.ts");

    public static TheoryData<string> Hosts => new()
    {
        Path.Combine("src", "Rask.Server", "Resources", "rask.ts"),
        Path.Combine("src", "Rask.Wasm", "Resources", "rask.wasm.ts"),
    };

    [Fact]
    public void The_reviver_turns_a_callback_marker_into_a_call_back_into_dotnet()
    {
        var source = Read(Bridge);

        var bridge = source.Substring(source.IndexOf("function scopedCallback", StringComparison.Ordinal));

        Assert.Contains("typeof shape.__raskCb__ === \"number\"", source);
        Assert.Contains("return scopedCallback(shape.__raskCb__)", source);
        Assert.Contains("invokeMethodAsync(\"Rask.Core\", \"RaskScopedCallback\", id, args)", bridge);
    }

    [Fact]
    public void Disposing_an_object_handle_drops_the_object_the_host_held()
    {
        var source = Read(Bridge);

        var dispose = source.Substring(source.IndexOf("disposeJSObjectReferenceById(id: number)", StringComparison.Ordinal));

        Assert.Contains("jsObjectRefs.delete(id)", dispose.Substring(0, 120));
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Each_host_dispatches_through_the_shared_bridge_and_hands_dotnet_its_handles(string host)
    {
        var source = Read(host);

        var shim = source.Substring(source.IndexOf("window.DotNet = window.DotNet ||", StringComparison.Ordinal));

        Assert.Contains("from \"../../Rask.Core/Resources/rask-js-invoke.js\"", source);
        Assert.Contains("invokeJs(", source);
        Assert.Contains("disposeJSObjectReferenceById,", shim);
        Assert.Contains("createJSObjectReference", shim);
    }

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}
