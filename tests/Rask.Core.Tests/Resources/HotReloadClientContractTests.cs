namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the dev-only hot-reload affordances in the client runtimes.
///     <para>
///         <b>What this is and isn't.</b> These are structural assertions over the shipped
///         TypeScript sources, not behavioural ones. The other client fixtures in this suite can
///         execute their subject in Node because <c>rask-morph.ts</c> / <c>rask-dom.ts</c> are
///         plain modules; <c>rask.ts</c> is a host entry point that boots a WebSocket against a
///         live document, so it cannot be loaded the same way. The behavioural proof — that an edit repaints
///         without a navigation and the indicator fires — is the watch E2E, which drives a real
///         browser. What is worth pinning here are the invariants that fail silently: a branch that
///         falls through, an ungated dev affordance, or a stray reload.
///     </para>
///     <para>
///         <b>On parity.</b> Both transports show the indicator, but each is <i>told</i> to differently —
///         Server on a pushed <c>hotReload</c> frame, WASM through the <c>hotReloadApplied()</c> export
///         called from .NET. So the contract is: one shared implementation, one trigger per transport. An
///         earlier version of this file forbade a per-transport copy outright and told whoever landed a
///         channel to hoist the indicator into a shared module instead; that is what happened, and these
///         tests now hold it to it.
///     </para>
/// </summary>
public class HotReloadClientContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ServerJs => Read("src", "Rask.Server", "Resources", "rask.ts");
    private static string WasmJs => Read("src", "Rask.Wasm", "Resources", "rask.wasm.ts");

    /// <summary>The single implementation every transport imports.</summary>
    private static string SharedJs => Read("src", "Rask.Core", "Resources", "rask-hotreload.ts");

    /// <summary>
    ///     The BUILT WASM bundle, which esbuild writes from the TypeScript above. Read here rather
    ///     than trusted: this is the file the browser actually loads, and asserting only on sources
    ///     would prove the inputs agree while saying nothing about what shipped.
    /// </summary>
    private static string BuiltWasmJs => Read("src", "Rask.Wasm", "Browser", "rask.wasm.js");

    [Fact]
    public void The_server_client_handles_the_hotReload_frame_and_returns()
    {
        var js = ServerJs;

        // The frame carries no html. Falling through to applyFullReply would morph the document
        // against a non-payload — the single worst outcome for a feature meant to be invisible.
        var branch = js[js.IndexOf("data.type === \"hotReload\"", StringComparison.Ordinal)..];
        var end = branch.IndexOf('}', StringComparison.Ordinal);
        Assert.Contains("return;", branch[..(end + 1)], StringComparison.Ordinal);
    }

    [Fact]
    public void The_hotReload_branch_is_gated_on_the_server_stamped_dev_flag()
    {
        var js = ServerJs;

        // devMode is read once at boot from an attribute the server only emits in Development, so
        // production HTML makes the affordance unreachable rather than merely unused.
        Assert.Contains("""root.hasAttribute("data-rask-dev")""", js, StringComparison.Ordinal);

        var branch = js[js.IndexOf("data.type === \"hotReload\"", StringComparison.Ordinal)..];
        var end = branch.IndexOf('}', StringComparison.Ordinal);
        Assert.Contains("devMode", branch[..(end + 1)], StringComparison.Ordinal);
    }

    [Fact]
    public void The_indicator_never_reloads_the_page()
    {
        // A reload defeats the entire feature — "hot reload that actually works" is defined against
        // exactly that. One assertion now covers both transports, because there is one source.
        Assert.DoesNotContain("location.reload", SharedJs, StringComparison.Ordinal);
    }

    [Fact]
    public void The_indicator_exposes_the_counter_the_watch_E2E_waits_on()
    {
        // The E2E asserts the feature rather than sleeping, by waiting for this to increment.
        Assert.Contains("window.__raskHotReloadCount", SharedJs, StringComparison.Ordinal);
    }

    [Fact]
    public void The_indicator_survives_a_morph()
    {
        // The pill and its <style> are framework-owned siblings of the rendered tree; without
        // data-rask-managed the next morph would trim them mid-animation. Both nodes need it — count
        // the actual calls, not the string, so a passing mention in a comment can't satisfy this.
        Assert.Equal(2, Occurrences(SharedJs, """setAttribute("data-rask-managed", "")"""));
    }

    [Fact]
    public void The_indicator_is_built_lazily_so_production_pays_nothing()
    {
        // Guarded by `if (!hotReloadPill)`, so a production bundle constructs no DOM and injects no
        // CSS for it — only the (unreachable) function body ships.
        var fn = SharedJs[SharedJs.IndexOf("function showHotReloadPill", StringComparison.Ordinal)..];

        Assert.Contains("if (!hotReloadPill)", fn[..400], StringComparison.Ordinal);
    }

    [Fact]
    public void The_indicator_is_one_shared_module_not_a_copy_per_dialect()
    {
        // The point of the hoist. Each host IMPORTS the one implementation — no host may grow its
        // own copy of the pill, which is how they would drift.
        foreach (var (name, js) in AllDialects())
        {
            Assert.True(
                js.Contains("showHotReloadPill } from", StringComparison.Ordinal)
                || js.Contains("showHotReloadPill,", StringComparison.Ordinal),
                $"{name} does not import the shared hot-reload indicator.");
            Assert.False(
                js.Contains("function showHotReloadPill", StringComparison.Ordinal),
                $"{name} declares its own showHotReloadPill — it must import the shared module instead.");
        }
    }

    [Fact]
    public void The_built_bundles_carry_the_shared_indicator()
    {
        // Asserted on the SHIPPED bytes, not on the sources they were bundled from. A source-only
        // check would pass on a bundler flag that dropped the module, or on a rename that left the
        // import resolving to something else — neither of which any source assertion can see.
        foreach (var (name, built) in BuiltArtifacts())
        {
            // The global, not the function name: a Release build minifies for real, so
            // `showHotReloadPill` is a single letter in the shipped bytes. The property it is
            // assigned to survives, and is the thing the E2E fixture reaches from the page.
            Assert.True(
                built.Contains("window.__raskHotReloadPill", StringComparison.Ordinal),
                $"{name} does not install the shared hot-reload indicator. Rebuild the project; if that "
                + "does not fix it, the bundle no longer reaches rask-hotreload.ts.");
        }
    }

    [Fact]
    public void Each_transport_triggers_the_indicator_its_own_way()
    {
        // Shared implementation, transport-specific trigger. Only Server has a socket a frame can
        // arrive on; WASM is called from .NET through a JS export.
        Assert.Contains("data.type === \"hotReload\"", ServerJs, StringComparison.Ordinal);
        Assert.Contains("showHotReloadPill()", ServerJs, StringComparison.Ordinal);

        Assert.Contains("export function hotReloadApplied", WasmJs, StringComparison.Ordinal);
        Assert.DoesNotContain("data.type === \"hotReload\"", WasmJs, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wasm_export_calls_the_imported_implementation_not_the_global()
    {
        // rask-hotreload.ts both exports showHotReloadPill and publishes it as
        // window.__raskHotReloadPill (the E2E fixture can only reach it from the page). This export
        // must call the IMPORT.
        //
        // Reading it off `window` here looked equivalent and was not. With nothing referencing the
        // import, TypeScript elided it, esbuild judged rask-hotreload.ts unreachable and dropped the
        // module from the bundle — side effect included — so the global was never assigned and the
        // defensive `if (window.__raskHotReloadPill)` this replaces silently swallowed every hot
        // reload. The guard did not merely fail to catch that; it is what caused it.
        var fn = WasmJs[WasmJs.IndexOf("export function hotReloadApplied", StringComparison.Ordinal)..];

        Assert.Contains("showHotReloadPill()", fn[..200], StringComparison.Ordinal);
        Assert.DoesNotContain("window.__raskHotReloadPill", fn[..200], StringComparison.Ordinal);
    }

    [Fact]
    public void The_dev_restart_shortcut_leaves_production_timing_untouched()
    {
        var js = ServerJs;

        // Production keeps the 4s grace and the accurate "timed out" wording; only the dev path is
        // shortened, and only because an unknown session under watch means a restart.
        Assert.Contains("const SESSION_EXPIRED_RELOAD_MS = 4000;", js, StringComparison.Ordinal);
        Assert.Contains("devMode ? DEV_RESTART_RELOAD_MS : SESSION_EXPIRED_RELOAD_MS", js, StringComparison.Ordinal);
    }

    private static IEnumerable<(string Name, string Js)> AllDialects()
    {
        yield return ("rask.ts", ServerJs);
        yield return ("rask.wasm.ts", WasmJs);
    }

    private static IEnumerable<(string Name, string Js)> BuiltArtifacts()
    {
        yield return ("src/Rask.Wasm/Browser/rask.wasm.js", BuiltWasmJs);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { _repoRoot }.Concat(parts).ToArray()));

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
