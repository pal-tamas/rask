namespace Rask.Wasm.Tests.JsInteropRuntime;

// Guards the runtime-script-side half of the scoped-JS race fix. WasmJSRuntime
// sends "Rask.{TypeName}.{method}" identifiers, but the browser-side bundle
// that defines window.Rask.{TypeName} is only injected after the first
// applyRender. Without a gate in rask.wasm.js, a first-render OnRenderedAsync
// invoking Rask.* fails with "Could not find ... on target" before the bundle
// is in the DOM. This test asserts the gate primitives are still present —
// silent deletion (e.g. during a mechanical rewrite) would re-open the race.
public sealed class JsBundleGateTests
{
    [Fact]
    public void RaskWasmJs_Has_ScopedJsReady_Gate()
    {
        var js = ReadBrowserBundle();
        Assert.Contains("scopedJsReady", js);
        Assert.Contains("pendingScopedInvokes", js);
    }

    [Fact]
    public void RaskWasmJs_BeginInvokeJS_Defers_RaskPrefixed_Identifiers()
    {
        var js = ReadBrowserBundle();
        var beginIdx = js.IndexOf("function beginInvokeJS", StringComparison.Ordinal);
        Assert.True(beginIdx >= 0, "beginInvokeJS function not found in bundle");
        var bodyEnd = js.IndexOf("Promise.resolve()", beginIdx, StringComparison.Ordinal);
        Assert.True(bodyEnd > beginIdx, "beginInvokeJS body not located");
        var prelude = js.Substring(beginIdx, bodyEnd - beginIdx);
        Assert.Contains("scopedJsReady", prelude);
        Assert.Contains("\"Rask.\"", prelude);
        Assert.Contains("pendingScopedInvokes.push", prelude);
    }

    [Fact]
    public void RaskWasmJs_MaybeDrainPendingInvokes_ReentersBeginInvokeJs()
    {
        // The legacy applyScopedJs that inlined a bundle <script> and flipped
        // scopedJsReady is gone — per-component scripts load via standard
        // <script src="/_rask/a/{hash}.js" defer> in <head>, and scopedJsReady
        // is initialized true. The drain path still matters: when a user-Head
        // declared CDN script finishes loading (or times out), the
        // trackHeadAsset finish handler calls maybeDrainPendingInvokes, which
        // must re-enter beginInvokeJS so queued Rask.* invokes resolve through
        // the original code path.
        var js = ReadBrowserBundle();
        var drainIdx = js.IndexOf("function maybeDrainPendingInvokes", StringComparison.Ordinal);
        Assert.True(drainIdx >= 0, "maybeDrainPendingInvokes function not found in bundle");
        var drainEnd = js.IndexOf("\n}\n", drainIdx, StringComparison.Ordinal);
        Assert.True(drainEnd > drainIdx);
        var drainBody = js.Substring(drainIdx, drainEnd - drainIdx);
        Assert.Contains("pendingScopedInvokes", drainBody);
        Assert.Contains("beginInvokeJS(", drainBody);
    }

    [Fact]
    public void RaskWasmJs_GatesRaskInvokes_OnHeadAssetLoad()
    {
        // Regression: Rask.* invokes must also wait for Head-declared external
        // <script src>/<link rel=stylesheet> to load. Without this, a
        // CodeSample-like component would have to hand-roll its own load-event
        // workaround (e.g. attaching a load listener to the hljs script).
        var js = ReadBrowserBundle();
        Assert.Contains("pendingHeadAssets", js);
        Assert.Contains("trackHeadAsset", js);
        Assert.Contains("scanHeadAssets", js);
        Assert.Contains("headAssetsReady", js);

        // beginInvokeJS must consult headAssetsReady() (not just scopedJsReady)
        // when deciding whether to park a Rask.* identifier.
        var beginIdx = js.IndexOf("function beginInvokeJS", StringComparison.Ordinal);
        Assert.True(beginIdx >= 0);
        var promiseIdx = js.IndexOf("Promise.resolve()", beginIdx, StringComparison.Ordinal);
        Assert.True(promiseIdx > beginIdx);
        var prelude = js.Substring(beginIdx, promiseIdx - beginIdx);
        Assert.Contains("headAssetsReady", prelude);
    }

    private static string ReadBrowserBundle()
    {
        var repoRoot = LocateRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "src", "Rask.Wasm", "Browser", "rask.wasm.js"));
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
