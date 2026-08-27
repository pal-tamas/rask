namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the build-status poll (#603) — the client half of "a compile error should
///     look like a compile error, not like a dropped connection".
///     <para>
///         Structural, for the same reason as <see cref="HotReloadClientContractTests" />: <c>rask.js</c> is
///         an IIFE that boots a socket against a live document and still carries its <c>@@RASK_*@@</c>
///         markers in the Resources copy, so it cannot be loaded in Node. The behavioural proof is manual —
///         break a file under <c>rask dev</c> and look. What is pinned here is what would fail <i>silently</i>:
///         a poll that never stops, a hard-coded endpoint, or a production page that polls localhost.
///     </para>
/// </summary>
public class BuildStatusClientContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ServerJs => Read("src", "Rask.Server", "Resources", "rask.ts");

    /// <summary>The single implementation every transport splices in.</summary>
    private static string SharedJs => Read("src", "Rask.Core", "Resources", "rask-deverror.ts");

    [Fact]
    public void The_drop_handler_asks_whether_it_is_a_compile_problem_before_claiming_a_network_one()
    {
        var js = ServerJs;
        var handler = js[js.IndexOf("function scheduleReconnect", StringComparison.Ordinal)..];
        var end = handler.IndexOf("\n    }", StringComparison.Ordinal);

        // Inside scheduleReconnect specifically: polling from anywhere else would either miss the drop
        // or run while the app is perfectly healthy.
        Assert.Contains("pollDevStatus(", handler[..end], StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_build_takes_down_the_reconnect_overlay()
    {
        // "Reconnecting…" over a "Retry now" button that cannot possibly succeed is the misleading half
        // of the issue. While the code does not compile, the compiler errors are the only thing on screen.
        var js = ServerJs;
        var call = js[js.IndexOf("pollDevStatus(", StringComparison.Ordinal)..];

        Assert.Contains("hideOverlay()", call[..600], StringComparison.Ordinal);
    }

    [Fact]
    public void The_backoff_keeps_running_underneath_so_a_fixed_build_recovers_on_its_own()
    {
        // The poll suppresses the overlay; it must not suppress the reconnect. Nobody should have to
        // reload the page after fixing a typo.
        var js = ServerJs;
        var after = js[js.IndexOf("pollDevStatus(", StringComparison.Ordinal)..];

        Assert.Contains("reconnectTimer = setTimeout", after, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_comes_from_the_document_not_from_a_constant()
    {
        // `rask dev` binds an OS-assigned port, so there is no URL to hard-code — and reading it from the
        // last page the server served is what makes it survive that server's death.
        Assert.Contains("""getAttribute("data-rask-dev-status")""", SharedJs, StringComparison.Ordinal);
        Assert.DoesNotContain("http://127.0.0.1:", SharedJs, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_with_no_status_url_never_polls()
    {
        // Production is exactly this case: no attribute, so `fetchDevStatus` resolves null without ever
        // touching the network, and the reconnect overlay behaves as it always did.
        var fn = SharedJs[SharedJs.IndexOf("function fetchDevStatus", StringComparison.Ordinal)..];

        Assert.Contains("if (!url", fn[..300], StringComparison.Ordinal);
        Assert.Contains("Promise.resolve(null)", fn[..300], StringComparison.Ordinal);
    }

    [Fact]
    public void The_poll_stops_as_soon_as_the_build_is_not_failing()
    {
        // An unreachable endpoint returns null and a healthy one returns "ok"; both must end the loop.
        // A poll that outlives its reason would hammer localhost for the life of the tab.
        var fn = SharedJs[SharedJs.IndexOf("function pollDevStatus", StringComparison.Ordinal)..];

        Assert.Contains("!status || status.state !== \"failed\"", fn, StringComparison.Ordinal);
        Assert.Contains("devStatusPolling = false;", fn, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_one_poll_loop_can_be_in_flight()
    {
        // Every reconnect attempt calls it, and the ladder retries five times.
        var fn = SharedJs[SharedJs.IndexOf("function pollDevStatus", StringComparison.Ordinal)..];

        Assert.Contains("if (devStatusPolling) return;", fn[..200], StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_fetch_is_not_an_error_the_developer_has_to_see()
    {
        // Under `rask dev` the endpoint is there; everywhere else it is not, and a console full of
        // failed polls would be noise about a feature that is correctly doing nothing.
        var fn = SharedJs[SharedJs.IndexOf("function fetchDevStatus", StringComparison.Ordinal)..];

        Assert.Contains(".catch(", fn[..400], StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_is_reused_rather_than_stacked_while_the_failure_persists()
    {
        // The poll re-reports the same failure every 700 ms. Counting those would climb forever and read
        // as "your app is getting worse".
        var fn = SharedJs[SharedJs.IndexOf("function showDevError", StringComparison.Ordinal)..];

        Assert.Contains("""if (info.kind === "build")""", fn, StringComparison.Ordinal);
        Assert.Contains("devErrorCount = 0;", fn, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { _repoRoot }.Concat(parts).ToArray()));

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
