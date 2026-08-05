namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the dev-only hot-reload affordances in the client runtimes.
///     <para>
///         <b>What this is and isn't.</b> These are structural assertions over the shipped
///         <c>.js</c> sources, not behavioural ones. The other client fixtures in this suite can
///         execute their subject in Node because <c>rask-morph.js</c> / <c>rask-dom.js</c> are
///         plain modules; <c>rask.js</c> is an IIFE that boots a WebSocket against a live document
///         and still carries its unsubstituted <c>@@RASK_*@@</c> splice markers in the Resources
///         copy, so it cannot be loaded the same way. The behavioural proof — that an edit repaints
///         without a navigation and the indicator fires — is the watch E2E, which drives a real
///         browser. What is worth pinning here are the invariants that fail silently: a branch that
///         falls through, an ungated dev affordance, or a stray reload.
///     </para>
///     <para>
///         <b>On parity.</b> All three transports now show the indicator, but each is <i>told</i> to
///         differently — Server on a pushed <c>hotReload</c> frame, WASM through the
///         <c>hotReloadApplied()</c> export called from .NET, Native over the WebView bridge. So the
///         contract is: one shared implementation, three transport-specific triggers. The earlier
///         version of this file forbade the WASM/Native branch outright and told whoever landed a
///         channel to hoist the indicator into a shared module rather than copy it; that is what
///         happened, and these tests now hold it to it.
///     </para>
/// </summary>
public class HotReloadClientContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ServerJs => Read("src", "Rask.Server", "Resources", "rask.js");
    private static string WasmJs => Read("src", "Rask.Wasm", "Resources", "rask.wasm.js");
    private static string NativeJs => Read("src", "Rask.Native", "Resources", "rask.native.js");

    /// <summary>The single implementation every transport splices in.</summary>
    private static string SharedJs => Read("src", "Rask.Core", "Resources", "rask-hotreload.js");

    // The two built artifacts that are committed (the Server's is assembled into obj/ and embedded, so
    // there is no checked-in copy to inspect).
    private static string BuiltWasmJs => Read("src", "Rask.Wasm", "Browser", "rask.wasm.js");
    private static string BuiltNativeJs => Read("src", "Rask.Native", "Assets", "rask.native.js");

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
        // exactly that. One assertion now covers all three transports, because there is one source.
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
    public void The_indicator_is_one_shared_module_not_three_copies()
    {
        // The point of the hoist. Each dialect carries the splice marker and nothing else — no
        // dialect may grow its own copy of the pill, which is how the three would drift.
        foreach (var (name, js) in AllDialects())
        {
            Assert.True(
                js.Contains("// @@RASK_HOTRELOAD@@", StringComparison.Ordinal),
                $"{name} does not splice the shared hot-reload indicator.");
            Assert.False(
                js.Contains("function showHotReloadPill", StringComparison.Ordinal),
                $"{name} declares its own showHotReloadPill — it must splice the shared module instead.");
        }
    }

    [Fact]
    public void The_committed_built_artifacts_carry_the_spliced_indicator()
    {
        // src/Rask.Wasm/Browser/rask.wasm.js and src/Rask.Native/Assets/rask.native.js are generated
        // by the build and COMMITTED. Editing the shared module without rebuilding leaves them stale,
        // which no other test would catch — the marker would still be sitting there unsubstituted.
        foreach (var (name, built) in BuiltArtifacts())
        {
            Assert.True(
                built.Contains("window.__raskHotReloadPill = showHotReloadPill", StringComparison.Ordinal),
                $"{name} is stale — rebuild its project to re-run the splice, and commit the result.");
            Assert.False(
                built.Contains("@@RASK_HOTRELOAD@@", StringComparison.Ordinal),
                $"{name} still contains an unsubstituted splice marker.");
        }
    }

    [Fact]
    public void Each_transport_triggers_the_indicator_its_own_way()
    {
        // Shared implementation, transport-specific trigger. Only Server has a socket a frame can
        // arrive on; WASM is called from .NET through a JS export; Native comes over the WebView
        // bridge, so its template needs no trigger of its own at all.
        Assert.Contains("data.type === \"hotReload\"", ServerJs, StringComparison.Ordinal);
        Assert.Contains("window.__raskHotReloadPill()", ServerJs, StringComparison.Ordinal);

        Assert.Contains("export function hotReloadApplied", WasmJs, StringComparison.Ordinal);
        Assert.DoesNotContain("data.type === \"hotReload\"", WasmJs, StringComparison.Ordinal);
        Assert.DoesNotContain("data.type === \"hotReload\"", NativeJs, StringComparison.Ordinal);
    }

    [Fact]
    public void The_indicator_cannot_throw_across_the_interop_boundary()
    {
        // WASM's export is called from .NET. If a future build ever drops the splice, a missing pill
        // must not surface as a failed hot reload — hence the guard rather than a bare call.
        var fn = WasmJs[WasmJs.IndexOf("export function hotReloadApplied", StringComparison.Ordinal)..];

        Assert.Contains("if (window.__raskHotReloadPill)", fn[..200], StringComparison.Ordinal);
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
        yield return ("rask.js", ServerJs);
        yield return ("rask.wasm.js", WasmJs);
        yield return ("rask.native.js", NativeJs);
    }

    private static IEnumerable<(string Name, string Js)> BuiltArtifacts()
    {
        yield return ("src/Rask.Wasm/Browser/rask.wasm.js", BuiltWasmJs);
        yield return ("src/Rask.Native/Assets/rask.native.js", BuiltNativeJs);
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
