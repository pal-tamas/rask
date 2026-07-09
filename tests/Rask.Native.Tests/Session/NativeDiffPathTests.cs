using System.Text.Json;
using Rask.Core.Live;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

// The diff codec (LiveDiffMode.Auto) runs identically over the native transport: after the initial
// full-HTML frame, an in-place state change ships a {kind:"diff", ops:[...]} payload the native client's
// applyDiff consumes — the same wire shape the Server and WASM hosts emit.
[Collection("NativeSession")]
public class NativeDiffPathTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task ClickAfterInitialRender_ShipsADiffFrame_NotFullHtml()
    {
        var (_, webView, initial) = await NewSessionAsync();

        // The initial render is full-HTML (it establishes the baseline); read its handler id from there.
        using (var initialDoc = JsonDocument.Parse(initial.AsMemory()))
        {
            Assert.False(initialDoc.RootElement.TryGetProperty("kind", out _),
                "the first frame should be full-HTML, not a diff");
        }

        var handlerId = Markup.FirstHandlerId(initial);
        await webView.PostAsync($$"""{"id":"{{handlerId}}","type":"click"}""");

        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.True(doc.RootElement.GetProperty("ops").GetArrayLength() > 0, "the diff should carry at least one op");
    }
}
