namespace Rask.Wasm.Tests.JsInteropRuntime;

// Guards the runtime-script-side half of the scoped-JS race fix. WasmJSRuntime
// sends "Rask.{TypeName}.{method}" identifiers, but the browser-side bundle
// that defines window.Rask.{TypeName} is only injected after the first
// applyRender. Without a gate in rask.wasm.ts, a first-render OnRenderedAsync
// invoking Rask.* fails with "Could not find ... on target" before the bundle
// is in the DOM. This test asserts the gate primitives are still present —
// silent deletion (e.g. during a mechanical rewrite) would re-open the race.
//
// Read from the TypeScript SOURCE, not from Browser/rask.wasm.js. Every name below is a local
// binding, and a Release build minifies the bundle for real now — `scopedJsReady` is `n` in the
// shipped bytes. These assertions read as artifact assertions and were, while "minification" meant a
// comment stripper that renamed nothing; against a real minifier they would pass or fail on which
// configuration happened to be built last, which is worse than not running at all.
//
// What the shipped bundle can still be held to — that the module reached it at all — is
// TheBundleShipsTheGate below, which asserts only on evidence minification preserves.
public sealed class JsBundleGateTests
{
    [Fact]
    public void RaskWasmJs_Has_ScopedJsReady_Gate()
    {
        var js = ReadSource();
        Assert.Contains("scopedJsReady", js);
        Assert.Contains("pendingScopedInvokes", js);
    }

    [Fact]
    public void RaskWasmJs_BeginInvokeJS_Defers_RaskPrefixed_Identifiers()
    {
        var js = ReadSource();
        var beginIdx = js.IndexOf("function beginInvokeJS", StringComparison.Ordinal);
        Assert.True(beginIdx >= 0, "beginInvokeJS function not found in rask.wasm.ts");
        var bodyEnd = js.IndexOf("Promise.resolve()", beginIdx, StringComparison.Ordinal);
        Assert.True(bodyEnd > beginIdx, "beginInvokeJS body not located in rask.wasm.ts");
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
        var js = ReadSource();
        var drainIdx = js.IndexOf("function maybeDrainPendingInvokes", StringComparison.Ordinal);
        Assert.True(drainIdx >= 0, "maybeDrainPendingInvokes function not found in rask.wasm.ts");
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
        var js = ReadSource();
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

    [Fact]
    public void TheBundleDoesNotTraceToTheConsole()
    {
        // The copy the browser actually downloads. `console.log` in a shipped runtime writes to every
        // visitor's console, and the payloads here carry whatever the user typed — so a trace left in
        // by accident is a privacy problem, not an untidiness.
        //
        // Asserted on the bundle rather than only on the sources it was built from: this project
        // references Rask.Wasm, so the bundle is built before this runs. The source halves live in
        // ClientConsoleContractTests, which cannot make that guarantee.
        var bundle = ReadBrowserBundle();

        var offenders = bundle
            .Split('\n')
            .Select((text, i) => (Line: i + 1, Text: text))
            .Where(l => l.Text.Contains("console.log", StringComparison.Ordinal))
            .Select(l => $"  rask.wasm.js:{l.Line}: {l.Text.Trim()}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "The built WASM bundle traces to console.log, which ships to production:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheBundleShipsTheGate()
    {
        // The one thing worth asking of the built artifact, and the only kind of thing that can be
        // asked of it: minification erases every local name above, but leaves string literals and
        // property names alone. The "Rask." discriminator is the gate's own literal — if the module
        // carrying it were dropped from the bundle (esbuild removes a module whose imports all went
        // unreferenced), this is what would notice.
        var bundle = ReadBrowserBundle();

        Assert.Contains("\"Rask.\"", bundle, StringComparison.Ordinal);
        Assert.Contains("window.__raskPainted", bundle, StringComparison.Ordinal);
    }

    private static string ReadSource()
    {
        var repoRoot = LocateRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "src", "Rask.Wasm", "Resources", "rask.wasm.ts"));
    }

    private static string ReadBrowserBundle()
    {
        var repoRoot = LocateRepoRoot();
        var path = Path.Combine(repoRoot, "src", "Rask.Wasm", "Browser", "rask.wasm.js");

        Assert.True(
            File.Exists(path),
            $"'{path}' is missing. It is build output now, not a tracked file — build Rask.Wasm first.");

        return File.ReadAllText(path);
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
