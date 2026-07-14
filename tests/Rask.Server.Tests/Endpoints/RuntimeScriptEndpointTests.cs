using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

public class RuntimeScriptEndpointTests
{
    [Fact]
    public async Task Get_RaskJs_ReturnsEmbeddedScriptWithJavaScriptContentType()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/rask/rask.js");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task Get_RaskJs_GatesRaskInvokes_OnHeadAssetLoad()
    {
        // Regression: Rask.* invokes must wait for Head-declared external
        // <script src>/<link rel=stylesheet> to load. Without this, a
        // CodeSample-like component would have to hand-roll its own load-event
        // workaround (e.g. attaching a load listener to the hljs script). The
        // gate primitives must be present in the served rask.js bundle.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js")).Content.ReadAsStringAsync();

        Assert.Contains("pendingHeadAssets", body);
        Assert.Contains("trackHeadAsset", body);
        Assert.Contains("scanHeadAssets", body);
        Assert.Contains("headAssetsReady", body);

        // dispatchJsInvoke must consult headAssetsReady() (not just
        // scopedJsReady) when deciding whether to park a Rask.* identifier.
        var dispatchIdx = body.IndexOf("function dispatchJsInvoke", StringComparison.Ordinal);
        Assert.True(dispatchIdx >= 0);
        // Peek the next ~600 chars — the gate check sits in the function prologue.
        var prelude = body.Substring(dispatchIdx, Math.Min(600, body.Length - dispatchIdx));
        Assert.Contains("headAssetsReady", prelude);
    }

    [Fact]
    public async Task Get_RaskJs_IncludesTransportAgnosticPwaHelpers()
    {
        // The PWA helpers shared from Rask.Core/Resources/rask-pwa.js must be spliced into the Server
        // client so IWebPush/INotifications/IBadge/IWakeLock can reach them.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js")).Content.ReadAsStringAsync();

        Assert.Contains("window.__raskPush =", body);
        Assert.Contains("window.__raskNotify =", body);
        Assert.Contains("window.__raskBadge =", body);
        Assert.Contains("window.__raskWakeLock =", body);
    }

    [Fact]
    public async Task Get_RaskJs_ExcludesWasmOnlyHelpers()
    {
        // Genuinely WASM-only helpers (manifest injection, the low-level device APIs) must NOT ship in the
        // Server client — they need boot behaviour / a hardware channel the WebSocket transport can't give.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("window.__raskPwa", body);
        Assert.DoesNotContain("window.__raskSerial", body);
        Assert.DoesNotContain("window.__raskBluetooth", body);
        Assert.DoesNotContain("window.__raskIdle", body);
    }

    [Fact]
    public async Task Get_RaskJs_IncludesGestureBridgeHelpers()
    {
        // The six gesture-bridge helpers moved into the shared rask-api.js so the declarative triggers
        // (FullscreenTrigger / ScreenOrientationTrigger / EyeDropperTrigger / InstallTrigger /
        // MediaCaptureTrigger / PictureInPictureTrigger) run their activation-gated API inside the click
        // gesture on the Server host too — where the imperative service can't be injected.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js")).Content.ReadAsStringAsync();

        Assert.Contains("window.__raskFullscreen", body);
        Assert.Contains("window.__raskEyeDropper", body);
        Assert.Contains("window.__raskOrientation", body);
        Assert.Contains("window.__raskInstall", body);
        Assert.Contains("window.__raskMedia", body);
        Assert.Contains("window.__raskPip", body);
        // …and the dispatch table wires their capabilities.
        Assert.Contains("orientation.lock", body);
        Assert.Contains("pip.request", body);
        Assert.Contains("install.prompt", body);
        Assert.Contains("media.start", body);
    }
}
