using System.Text;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Rask.Native.Tests;

// The reusable capability dispatcher a Native + Server head uses (and the Native + Local router shares):
// a { type:"capability" } envelope routes "share" to the supplied IShare; anything else is a no-op or a
// pass-through. BridgeScript is the document-start JS the head injects for its trusted origin.
public class NativeCapabilitiesTests
{
    [Fact]
    public async Task TryHandle_ShareCapability_DispatchesToShare_ReturnsTrue()
    {
        var share = new RecordingShare();

        var handled = await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","component":"share","data":"{\"title\":\"Rask\",\"url\":\"https://x\"}"}"""),
            share);

        Assert.True(handled);
        Assert.Equal("Rask", share.Last!.Title);
        Assert.Equal("https://x", share.Last.Url);
    }

    [Fact]
    public async Task TryHandle_NonCapabilityMessage_ReturnsFalse_WithoutDispatch()
    {
        var share = new RecordingShare();

        var handled = await NativeCapabilities.TryHandleAsync(Msg("""{"type":"event","id":"h0"}"""), share);

        Assert.False(handled);
        Assert.Null(share.Last);
    }

    [Fact]
    public async Task TryHandle_UnknownComponent_ConsumedAsNoOp_ReturnsTrue()
    {
        var share = new RecordingShare();

        var handled = await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","component":"bogus","data":"{}"}"""), share);

        Assert.True(handled);
        Assert.Null(share.Last);
    }

    [Fact]
    public async Task TryHandle_MalformedSharePayload_Discarded_NoThrow_ReturnsTrue()
    {
        var share = new RecordingShare();

        var handled = await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","component":"share","data":"not json"}"""), share);

        Assert.True(handled);
        Assert.Null(share.Last);
    }

    [Fact]
    public void BridgeScript_DefinesRaskNativeCapabilitiesAndInvokeOverTheSendChannel()
    {
        var script = NativeCapabilities.BridgeScript;

        Assert.Contains("window.__raskNative", script);
        Assert.Contains("capabilities", script);
        Assert.Contains("\"share\"", script);
        Assert.Contains("invoke", script);
        Assert.Contains("__raskSend", script);
    }

    private static byte[] Msg(string json) => Encoding.UTF8.GetBytes(json);

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
