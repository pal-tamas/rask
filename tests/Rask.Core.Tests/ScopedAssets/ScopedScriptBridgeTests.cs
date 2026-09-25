namespace Rask.Core.Tests.ScopedAssets;

// The browser half of ScopedScript, on BOTH hosts: a callback marker revives into a function that calls
// back into .NET, and .NET disposing an object handle lets the host drop the object. Read from the
// TypeScript source, as JsBundleGateTests does — the shipped bundle is minified.
public class ScopedScriptBridgeTests
{
    public static TheoryData<string> Hosts => new()
    {
        Path.Combine("src", "Rask.Server", "Resources", "rask.ts"),
        Path.Combine("src", "Rask.Wasm", "Resources", "rask.wasm.ts"),
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public void The_reviver_turns_a_callback_marker_into_a_call_back_into_dotnet(string host)
    {
        var source = Read(host);

        var bridge = source.Substring(source.IndexOf("function scopedCallback", StringComparison.Ordinal));

        Assert.Contains("typeof shape.__raskCb__ === \"number\"", source);
        Assert.Contains("return scopedCallback(shape.__raskCb__)", source);
        Assert.Contains("invokeMethodAsync(\"Rask.Core\", \"RaskScopedCallback\", id, args)", bridge);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Disposing_an_object_handle_drops_the_object_the_host_held(string host)
    {
        var source = Read(host);

        var dispose = source.Substring(source.IndexOf("disposeJSObjectReferenceById(id: number)", StringComparison.Ordinal));

        Assert.Contains("jsObjectRefs.delete(id)", dispose.Substring(0, 120));
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
