using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

public class RuntimeScriptEndpointTests
{
    [Fact]
    public async Task The_runtime_script_is_the_embedded_script_with_a_javascript_content_type()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/rask/rask.js", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task The_runtime_script_gates_Rask_invokes_on_head_asset_load()
    {
        // Regression: Rask.* invokes must wait for Head-declared external
        // <script src>/<link rel=stylesheet> to load. Without this, a
        // CodeSample-like component would have to hand-roll its own load-event
        // workaround (e.g. attaching a load listener to the hljs script).
        //
        // The gate's primitives are local bindings, and Release minifies the served runtime for
        // real — `headAssetsReady` is a single letter in the shipped bytes. So the structure is
        // asserted where it is legible, in rask.ts (HeadAssetGateSourceTests), and what is asked of
        // the SERVED script here is the part minification preserves: the "Rask." discriminator the
        // gate keys on, which is only in the bundle if the gate's module is.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"Rask.\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_runtime_script_includes_Rask_Webs_browser_patches()
    {
        // Rask.Web's named patches (rask-web-patches.ts) run in the shared runtime, so a Server app's wake lock,
        // speech recognition and install prompt get them too. Push, notifications and the badge are MDN's own,
        // from Rask.Web, so no helper of Rask's stands in for them.
        //
        // Asserted on string literals and property names, which a minifier leaves alone.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("webkitSpeechRecognition", body, StringComparison.Ordinal);
        Assert.Contains("WakeLock.request", body, StringComparison.Ordinal);
        Assert.Contains("beforeinstallprompt", body, StringComparison.Ordinal);
        Assert.DoesNotContain("window.__raskWakeLock", body, StringComparison.Ordinal);
        Assert.DoesNotContain("window.__raskPush", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_runtime_script_excludes_the_WASM_only_helpers()
    {
        // Genuinely WASM-only helpers (manifest injection, the low-level device APIs) must NOT ship in the
        // Server client — they need boot behaviour / a hardware channel the WebSocket transport can't give.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("window.__raskPwa", body);
        Assert.DoesNotContain("window.__raskSerial", body);
        Assert.DoesNotContain("window.__raskBluetooth", body);
        Assert.DoesNotContain("window.__raskIdle", body);
    }

    [Fact]
    public async Task The_runtime_script_includes_the_gesture_bridge_helpers()
    {
        // The six gesture-bridge helpers moved into the shared rask-api.js so the declarative triggers
        // (Trigger.Fullscreen / Trigger.ScreenOrientation / Trigger.EyeDropper / Trigger.Install /
        // Trigger.MediaCapture / Trigger.PictureInPicture) run their activation-gated API inside the click
        // gesture on the Server host too — where the imperative service can't be injected.
        using var host = RaskTestHost.Create<TestApp>();

        var body = await (await host.Http.GetAsync("/rask/rask.js", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

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
