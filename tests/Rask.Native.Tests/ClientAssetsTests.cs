using Rask.Native;

namespace Rask.Native.Tests;

// Guards the build-time splice + embed pipeline for the native client dialect: the shared Rask.Core
// modules must be spliced into rask.native.js (no leftover @@RASK_*@@ markers), the native transport shim
// must be present, and the boot shell must carry the native root id.
public class ClientAssetsTests
{
    [Fact]
    public void ClientJs_HasNoUnreplacedSpliceMarkers()
    {
        // A splice point is a marker on its own line; assert none survived. (A shared module may *mention*
        // a marker inside a documentation comment — e.g. rask-events.js references "// @@RASK_EVENTS@@" —
        // which is not a splice point, so match on the trimmed whole line, not any substring.)
        string[] markers = ["// @@RASK_DOM@@", "// @@RASK_MORPH@@", "// @@RASK_API@@", "// @@RASK_EVENTS@@", "// @@RASK_PWA@@"];
        foreach (var line in NativeClientAssets.ClientJs.Split('\n'))
        {
            Assert.DoesNotContain(line.Trim(), markers);
        }
    }

    [Fact]
    public void ClientJs_SplicesTheSharedDiffAndMorphModules()
    {
        var js = NativeClientAssets.ClientJs;
        Assert.Contains("function applyDiff", js);
        Assert.Contains("function morph", js);
        Assert.Contains("__raskApi", js);
    }

    [Fact]
    public void ClientJs_ExposesTheNativeTransportBridge()
    {
        var js = NativeClientAssets.ClientJs;
        Assert.Contains("window.__raskNative", js);
        Assert.Contains("window.__raskSend", js);
        Assert.Contains("beginInvokeJS", js);
        Assert.Contains("endDotNetInvoke", js);
    }

    [Fact]
    public void IndexHtml_CarriesTheNativeRootAndLoadsTheClient()
    {
        var html = NativeClientAssets.IndexHtml;
        Assert.Contains("data-rask-root=\"native\"", html);
        Assert.Contains("rask.native.js", html);
    }
}
