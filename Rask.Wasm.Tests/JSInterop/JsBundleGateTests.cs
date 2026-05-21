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
    public void RaskWasmJs_ApplyScopedJs_Drains_Pending_Queue()
    {
        var js = ReadBrowserBundle();
        var applyIdx = js.IndexOf("function applyScopedJs", StringComparison.Ordinal);
        Assert.True(applyIdx >= 0, "applyScopedJs function not found in bundle");
        var applyEnd = js.IndexOf("\n}\n", applyIdx, StringComparison.Ordinal);
        Assert.True(applyEnd > applyIdx, "applyScopedJs body not located");
        var body = js.Substring(applyIdx, applyEnd - applyIdx);
        // The drain must happen after the script injection (otherwise replayed
        // calls would resolve against a still-empty window.Rask) and must
        // re-enter beginInvokeJS so the original code path runs unchanged.
        Assert.Contains("scopedJsReady = true", body);
        Assert.Contains("pendingScopedInvokes", body);
        Assert.Contains("beginInvokeJS(", body);
    }

    private static string ReadBrowserBundle()
    {
        var repoRoot = LocateRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "Rask.Wasm", "Browser", "rask.wasm.js"));
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.sln walking up from {AppContext.BaseDirectory}");
    }
}
