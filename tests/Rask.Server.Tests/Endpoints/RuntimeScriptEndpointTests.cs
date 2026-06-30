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
        // WASM-only helpers (manifest injection, install-prompt replay, device APIs) must NOT ship in the
        // Server client — they need transient activation / boot behaviour the WebSocket transport can't give.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("window.__raskInstall", body);
        Assert.DoesNotContain("window.__raskPwa", body);
        Assert.DoesNotContain("window.__raskSerial", body);
        Assert.DoesNotContain("window.__raskBluetooth", body);
    }
}
