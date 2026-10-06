using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

/// <summary>
///     An island callback fired before the WASM app has booted reaches C# once it has.
/// </summary>
/// <remarks>
///     A prerendered page mounts its islands from the served HTML, so they are clickable before .NET
///     exists. The Server host has always queued a callback fired while its socket was still opening;
///     this host dropped it with a console warning. Driven under Node against the built
///     <c>rask.wasm.js</c> — see <c>EarlyIslandCallbackFixture.ts</c>.
/// </remarks>
public sealed class EarlyIslandCallbackTests
{
    [Fact]
    public void An_island_callback_fired_before_boot_is_delivered_after_the_first_frame()
    {
        if (Run("delivered") is not { } result)
        {
            return;
        }

        Assert.Equal(0, result.GetProperty("dispatchedBeforeBoot").GetInt32());
        Assert.Equal(["h3"], Dispatched(result));
    }

    [Fact]
    public void A_queued_callback_is_not_delivered_to_a_different_island()
    {
        // Handler ids are positional and the prerendered page came from another process, so the same id
        // on a different island is a different handler — one the visitor never pressed.
        if (Run("other-island") is not { } result)
        {
            return;
        }

        Assert.Empty(Dispatched(result));
        Assert.Contains(Warnings(result), w => w.Contains("'Counter' island", StringComparison.Ordinal));
    }

    [Fact]
    public void The_pre_boot_queue_is_bounded()
    {
        if (Run("bounded") is not { } result)
        {
            return;
        }

        var dispatched = Dispatched(result);

        Assert.Equal(32, dispatched.Length);
        Assert.Equal("h8", dispatched[0]);
        Assert.Equal("h39", dispatched[^1]);
    }

    [Fact]
    public void A_dom_event_before_boot_is_still_dropped()
    {
        // Only an island is interactive before boot. A prerendered Rask control has no handler yet, and
        // replaying a click against the live page's positional ids could press something else (#973).
        if (Run("dom-event") is not { } result)
        {
            return;
        }

        Assert.Empty(Dispatched(result));
    }

    private static string[] Dispatched(JsonElement result) =>
        [.. result.GetProperty("dispatched").EnumerateArray().Select(e => e.GetString()!)];

    private static string[] Warnings(JsonElement result) =>
        [.. result.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()!)];

    // Null when node is not on PATH: the JS-driven check cannot run, and node is not required to build Rask.
    private static JsonElement? Run(string scenario)
    {
        var bundle = Path.Combine(RepoRoot(), "src", "Rask.Wasm", "Browser", "rask.wasm.js");
        Assert.True(File.Exists(bundle), $"Bundle missing: {bundle} — build src/Rask.Wasm first.");

        return NodeFixture.Run("EarlyIslandCallbackFixture", bundle, scenario);
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
