namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the graceful-shutdown affordances in the client runtimes. Structural
///     assertions over the shipped <c>.js</c>, for the same reason as
///     <see cref="HotReloadClientContractTests" />: <c>rask.js</c> is an IIFE that boots a WebSocket
///     against a live document and still carries its unsubstituted <c>@@RASK_*@@</c> splice markers in
///     the Resources copy, so it cannot be executed in Node. What is worth pinning here are the
///     invariants that fail silently — a branch that falls through, a gate that shouldn't be there, a
///     restore that never gets consumed.
/// </summary>
public class ShutdownClientContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ServerJs => Read("src", "Rask.Server", "Resources", "rask.js");
    private static string WasmJs => Read("src", "Rask.Wasm", "Resources", "rask.wasm.js");
    private static string NativeJs => Read("src", "Rask.Native", "Resources", "rask.native.js");

    [Fact]
    public void The_server_client_handles_the_shutdown_frame_and_returns()
    {
        var js = ServerJs;

        // Like the hotReload frame, this one carries no html. Falling through to applyFullReply would
        // morph the document against a non-payload.
        var branch = js[js.IndexOf("data.type === \"shutdown\"", StringComparison.Ordinal)..];
        var end = branch.IndexOf("\n            }", StringComparison.Ordinal);
        Assert.Contains("return;", branch[..end], StringComparison.Ordinal);
    }

    [Fact]
    public void The_shutdown_branch_is_not_dev_gated()
    {
        // The opposite of the hotReload contract, and deliberately so: a production redeploy is exactly
        // when this matters. Gating it on devMode would leave production showing "Your session timed
        // out" on every deploy — the bug this whole path exists to fix.
        var js = ServerJs;
        var branch = js[js.IndexOf("data.type === \"shutdown\"", StringComparison.Ordinal)..];
        var end = branch.IndexOf("\n            }", StringComparison.Ordinal);

        Assert.DoesNotContain("devMode", branch[..end], StringComparison.Ordinal);
    }

    [Fact]
    public void A_going_away_close_is_treated_as_a_deployment()
    {
        // Belt and braces for a missed frame: 1001 is the drain's own close status. Before this, the
        // close code was never inspected at all — close and error shared one handler.
        Assert.Contains("e.code === 1001", ServerJs, StringComparison.Ordinal);
    }

    [Fact]
    public void A_known_redeploy_reconnects_rather_than_reloading()
    {
        // The load-bearing property. Reloading straight from the close handler would throw away whatever
        // the reconnect could still recover — it is the reconnect that carries the client's state to a
        // host which never knew this session, so only the server's answer may decide the page is lost.
        var js = ServerJs;
        var fn = js[js.IndexOf("function scheduleReconnect", StringComparison.Ordinal)..];
        var branch = fn[..fn.IndexOf("\n    }", StringComparison.Ordinal)];

        var fastPath = branch[..branch.IndexOf("open = false;\n        resetPending();", StringComparison.Ordinal)];
        Assert.Contains("connect();", fastPath, StringComparison.Ordinal);
        Assert.DoesNotContain("location.reload", fastPath, StringComparison.Ordinal);
        Assert.DoesNotContain("reloadForUpdate()", fastPath, StringComparison.Ordinal);
    }

    [Fact]
    public void The_immediate_retry_happens_once_so_a_missing_replacement_cannot_spin()
    {
        // Every failed connect closes, and every close re-enters scheduleReconnect. Without a latch the
        // redeploy branch would take itself again forever, hammering a host that is not up yet.
        var js = ServerJs;
        var fn = js[js.IndexOf("function scheduleReconnect", StringComparison.Ordinal)..];
        var branch = fn[..fn.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("!shutdownRetryUsed", branch, StringComparison.Ordinal);
        Assert.Contains("shutdownRetryUsed = true;", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reload_is_reachable_only_from_the_servers_own_answer()
    {
        // reloadForUpdate is the fallback for "we reconnected and the new host could not rebuild us",
        // which is showSessionExpired's job to detect. Any other caller would be pre-empting the server.
        var js = ServerJs;
        // The trailing semicolon is what distinguishes a call from the `function reloadForUpdate() {`
        // declaration.
        var callSites = js.Split("reloadForUpdate();", StringSplitOptions.None).Length - 1;

        Assert.Equal(1, callSites);
        var expired = js[js.IndexOf("function showSessionExpired", StringComparison.Ordinal)..];
        Assert.Contains("reloadForUpdate();", expired[..400], StringComparison.Ordinal);
    }

    [Fact]
    public void A_redeploy_keeps_its_wording_but_still_offers_a_way_out()
    {
        // Two things at once, and the second is the easy one to lose. The drop is explained, so escalating
        // to "Still trying to reconnect…" would report something broken — but a deploy CAN fail, and
        // leaving the user frozen on "Updating…" with nothing to click would be worse than the reconnect
        // state this replaced. Keep the wording, keep the escape hatch.
        var js = ServerJs;
        var fn = js[js.IndexOf("function updateOverlayState", StringComparison.Ordinal)..];
        var body = fn[..fn.IndexOf("\n    }", StringComparison.Ordinal)];
        var branch = body[body.IndexOf("if (serverShuttingDown)", StringComparison.Ordinal)..];
        var branchEnd = branch[..branch.IndexOf("return;", StringComparison.Ordinal)];

        Assert.Contains("setOverlayMessage(UPDATING_MSG)", branchEnd, StringComparison.Ordinal);
        Assert.Contains("setRetryButton(escalated ? \"Retry now\" : null)", branchEnd, StringComparison.Ordinal);
        Assert.DoesNotContain("Still trying to reconnect", branchEnd, StringComparison.Ordinal);
    }

    [Fact]
    public void The_update_reload_says_updating_not_timed_out()
    {
        var js = ServerJs;
        var fn = js[js.IndexOf("function reloadForUpdate", StringComparison.Ordinal)..];
        var end = fn.IndexOf("\n    }", StringComparison.Ordinal);

        Assert.Contains("UPDATING_MSG", fn[..end], StringComparison.Ordinal);
        Assert.Contains("saveRestorePoint()", fn[..end], StringComparison.Ordinal);
        // Fast, because a blue-green swap has already moved the proxy to the replacement.
        Assert.Contains("const SHUTDOWN_RELOAD_MS = 250;", js, StringComparison.Ordinal);
    }

    [Fact]
    public void The_restore_point_is_consumed_even_when_it_does_not_apply()
    {
        // A restore point that survives its own reload would later yank an unrelated navigation back to
        // a stale scroll position. The removeItem must therefore run before any early return.
        var js = ServerJs;
        var fn = js[js.IndexOf("function applyRestorePoint", StringComparison.Ordinal)..];
        var body = fn[..fn.IndexOf("\n    }", StringComparison.Ordinal)];

        var removed = body.IndexOf("removeItem", StringComparison.Ordinal);
        var firstGuardedReturn = body.IndexOf("if (!saved) return;", StringComparison.Ordinal);

        Assert.True(removed >= 0 && firstGuardedReturn > removed,
            "the restore point must be consumed before the first early return");
    }

    [Fact]
    public void The_restore_point_carries_no_form_values()
    {
        // Scroll and focus only. The replacement server renders field values from its own state;
        // writing stale client copies over them would turn a cosmetic loss into a data one.
        var js = ServerJs;
        var fn = js[js.IndexOf("function saveRestorePoint", StringComparison.Ordinal)..];
        var body = fn[..fn.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.DoesNotContain(".value", body, StringComparison.Ordinal);
        Assert.Contains("scrollY", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_server_dialect_branches_on_the_shutdown_frame()
    {
        // Same asymmetry as the hotReload contract, for the same reason: WASM runs the session
        // in-browser and Native is fed by its host over the WebView bridge, so neither has a server
        // whose shutdown could reach them. Neither file contains a WebSocket at all.
        Assert.Contains("data.type === \"shutdown\"", ServerJs, StringComparison.Ordinal);
        Assert.DoesNotContain("\"shutdown\"", WasmJs, StringComparison.Ordinal);
        Assert.DoesNotContain("\"shutdown\"", NativeJs, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([_repoRoot, .. parts]));

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
