using System.Text.Json;
using Rask.Core.Live;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

// Full-HTML wire shape (LiveDiffMode.DisabledFull) so tests can assert against the `html` payload field.
[Collection("NativeSession")]
public class NativeDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task ReadyHandshake_TriggersFirstRender_WithNativeRootId()
    {
        var (_, _, initial) = await NewSessionAsync(diffMode: DiffMode);

        using var doc = JsonDocument.Parse(initial.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("data-rask-root=\"native\"", html);
    }

    [Fact]
    public async Task NoRender_UntilReadyHandshakeArrives()
    {
        // Drive the host directly (bypass the harness, which posts `ready`) to prove the first frame is
        // gated on the client's readiness — a real WebView isn't ready to receive applyRender until loaded.
        var host = NativeAppHost.CreateDefault(o => o.DiffMode = DiffMode);
        var webView = new FakeNativeWebView();
        _ = await host.RunLocalAsync<NativeStubApp>(webView);

        Assert.Empty(webView.Frames);

        await webView.PostAsync("""{"type":"ready"}""");

        Assert.Single(webView.Frames);
    }

    [Fact]
    public async Task ClickHandler_IncrementsCounter_AndPushesUpdatedFrame()
    {
        var (_, webView, initial) = await NewSessionAsync(diffMode: DiffMode);
        var handlerId = Markup.FirstHandlerId(initial);

        await webView.PostAsync($$"""{"id":"{{handlerId}}","type":"click"}""");

        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("count=1", html);
    }

    [Fact]
    public async Task Navigate_UpdatesRouteState_AndReflectsNewPath()
    {
        var (_, webView, _) = await NewSessionAsync(diffMode: DiffMode);

        await webView.PostAsync("""{"type":"navigate","path":"/foo","query":""}""");

        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("path=/foo", html);
    }

    [Fact]
    public async Task MalformedMessage_IsSwallowed_NoThrow_NoExtraFrame()
    {
        var (_, webView, _) = await NewSessionAsync(diffMode: DiffMode);
        var framesBefore = webView.Frames.Count;

        await webView.PostAsync("this is not json");

        Assert.Equal(framesBefore, webView.Frames.Count);
    }

    [Fact]
    public async Task UnknownHandlerId_ProducesNoFrame()
    {
        var (_, webView, _) = await NewSessionAsync(diffMode: DiffMode);
        var framesBefore = webView.Frames.Count;

        await webView.PostAsync("""{"id":"h999","type":"click"}""");

        Assert.Equal(framesBefore, webView.Frames.Count);
    }
}
