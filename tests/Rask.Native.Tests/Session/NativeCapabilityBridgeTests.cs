using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Core.Browser;
using Rask.Core.Live;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

// The native capability bridge: window.__raskNative.invoke(component, data) posts a { type:"capability" }
// message the host routes to the registered service. This is how the declarative Shareable (and a
// native-shell page) reaches the native backend the head registered as IShare — no user activation needed.
[Collection("NativeSession")]
public class NativeCapabilityBridgeTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task Capability_Share_RoutesToRegisteredIShare_WithDeserializedPayload()
    {
        var share = new RecordingShare();
        var (_, webView, _) = await NewSessionAsync(
            configure: s => s.AddSingleton<IShare>(share), diffMode: DiffMode);

        await webView.PostAsync(
            """{"type":"capability","component":"share","data":"{\"title\":\"Rask\",\"url\":\"https://x\"}"}""");

        Assert.NotNull(share.Last);
        Assert.Equal("Rask", share.Last!.Title);
        Assert.Equal("https://x", share.Last.Url);
    }

    [Fact]
    public async Task Capability_UnknownComponent_NoOps()
    {
        var share = new RecordingShare();
        var (_, webView, _) = await NewSessionAsync(
            configure: s => s.AddSingleton<IShare>(share), diffMode: DiffMode);

        await webView.PostAsync("""{"type":"capability","component":"bogus","data":"{}"}""");

        Assert.Null(share.Last);
    }

    [Fact]
    public async Task Capability_MalformedPayload_IsDiscarded_NoThrow()
    {
        var share = new RecordingShare();
        var (_, webView, _) = await NewSessionAsync(
            configure: s => s.AddSingleton<IShare>(share), diffMode: DiffMode);

        await webView.PostAsync("""{"type":"capability","component":"share","data":"not json"}""");

        Assert.Null(share.Last);
    }

    private sealed class RecordingShare : IShare
    {
        public ShareData? Last { get; private set; }

        public ValueTask ShareAsync(ShareData data)
        {
            Last = data;
            return default;
        }

        public ValueTask<bool> CanShareAsync(ShareData? data = null) => ValueTask.FromResult(true);
    }
}
