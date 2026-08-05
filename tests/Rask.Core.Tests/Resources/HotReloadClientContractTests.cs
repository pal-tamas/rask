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
///         <b>On parity.</b> Only the Server transport receives server-pushed frames, so a naive
///         "all three dialects contain the branch" assertion would be wrong. WASM and Native have no
///         server to push one. The contract is deliberately asymmetric — see the tests below.
///     </para>
/// </summary>
public class HotReloadClientContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ServerJs => Read("src", "Rask.Server", "Resources", "rask.js");
    private static string WasmJs => Read("src", "Rask.Wasm", "Resources", "rask.wasm.js");
    private static string NativeJs => Read("src", "Rask.Native", "Resources", "rask.native.js");

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
    public void The_hotReload_path_never_reloads_the_page()
    {
        // A reload defeats the entire feature — "hot reload that actually works" is defined against
        // exactly that. Applies to all three dialects.
        foreach (var (name, js) in AllDialects())
        {
            var at = js.IndexOf("showHotReloadPill", StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var fn = js[at..];
            var end = fn.IndexOf("\n    }", StringComparison.Ordinal);
            Assert.DoesNotContain("location.reload", fn[..end], StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(name));
        }
    }

    [Fact]
    public void The_indicator_exposes_the_counter_the_watch_E2E_waits_on()
    {
        // The E2E asserts the feature rather than sleeping, by waiting for this to increment.
        Assert.Contains("window.__raskHotReloadCount", ServerJs, StringComparison.Ordinal);
    }

    [Fact]
    public void The_indicator_survives_a_morph()
    {
        // The pill is a framework-owned sibling of the server-rendered tree; without
        // data-rask-managed the next morph would trim it mid-animation.
        var js = ServerJs;
        var fn = js[js.IndexOf("function showHotReloadPill", StringComparison.Ordinal)..];
        var end = fn.IndexOf("\n    }", StringComparison.Ordinal);

        Assert.Contains("data-rask-managed", fn[..end], StringComparison.Ordinal);
    }

    [Fact]
    public void The_indicator_is_built_lazily_so_production_pays_nothing()
    {
        // Guarded by `if (!hotReloadPill)`, so a production bundle constructs no DOM and injects no
        // CSS for it — only the (unreachable) function body ships.
        var js = ServerJs;
        var fn = js[js.IndexOf("function showHotReloadPill", StringComparison.Ordinal)..];

        Assert.Contains("if (!hotReloadPill)", fn[..200], StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_server_dialect_branches_on_the_pushed_frame()
    {
        // WASM runs in-browser with no Rask server, and Native is fed by its host over the WebView
        // bridge — neither has a socket a hotReload frame could arrive on. Adding the branch there
        // would be dead code, and in WASM's case would also force a regenerated commit of the
        // checked-in built artifact (src/Rask.Wasm/Browser/rask.wasm.js).
        //
        // If a WASM/Native hot-reload channel ever lands, promote the indicator into a shared
        // rask-*.js module at that point — don't just copy this branch across.
        Assert.Contains("data.type === \"hotReload\"", ServerJs, StringComparison.Ordinal);
        Assert.DoesNotContain("hotReload", WasmJs, StringComparison.Ordinal);
        Assert.DoesNotContain("hotReload", NativeJs, StringComparison.Ordinal);
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
