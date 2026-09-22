namespace Rask.Wasm.Tests.JsInteropRuntime;

// Regression guard: a scoped-JS <script> that arrived in the SERVED document must not hold Rask.* invokes.
//
// A prerendered page carries its rsk-js <script defer> in <head> (the prerender companion now renders with
// the app's scoped assets registered). The parser runs it before the runtime is imported, so its load event
// is long gone by the time the invoke gate looks — and the gate waited for it anyway, parking every Rask.*
// call until the 30s backstop. Found by the showcase journey: "Measure the box" produced nothing within 10s.
//
// Driven under Node against the built rask.wasm.js (see PrerenderedScopedJsGateFixture.ts), with every
// backstop timer captured rather than run — an invoke that fires only because a backstop expired is the bug.
public sealed class PrerenderedScopedJsGateTests
{
    [Fact]
    public void A_scoped_script_the_parser_already_ran_does_not_hold_Rask_invokes()
    {
        var bundle = Path.Combine(RepoRoot(), "src", "Rask.Wasm", "Browser", "rask.wasm.js");
        Assert.True(File.Exists(bundle), $"Bundle missing: {bundle} — build src/Rask.Wasm first.");

        // No node on PATH — the JS-driven check cannot run. Not a failure: node is not required to build Rask.
        var result = NodeFixture.Run("PrerenderedScopedJsGateFixture", bundle);
        if (result is null)
        {
            return;
        }

        Assert.False(
            result.Value.GetProperty("servedScriptHeldTheInvoke").GetBoolean(),
            "A Rask.* invoke was parked behind a scoped <script> the served document had already executed; "
            + "it would only have run when the 30s backstop expired.");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Could not locate Rask.slnx above {AppContext.BaseDirectory}");
    }
}
