namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the head-asset gate both client runtimes share (<c>rask-head-assets.ts</c>).
/// </summary>
/// <remarks>
///     <para>
///         A <c>Rask.*</c> invoke must not dispatch until the assets a component declared in <c>Head</c> have settled
///         and its scoped script has defined <c>window.Rask.{Type}</c>. Without the gate, a first-render
///         <c>OnRendered</c> fails with "Could not find … on target".
///     </para>
///     <para>
///         The server and WASM runtimes each carried a copy of the gate, and only WASM's learned the cold-boot fixes;
///         these assert that both now go through the one module rather than growing a copy back.
///     </para>
///     <para>
///         <b>Why the source and not the served script.</b> Every name below is a local binding, and Release
///         minifies the served runtimes for real, so the shipped bytes cannot be asked about them.
///     </para>
/// </remarks>
public class HeadAssetGateSourceTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string GateTs => Read("src", "Rask.Core", "Resources", "rask-head-assets.ts");

    private static string ServerTs => Read("src", "Rask.Server", "Resources", "rask.ts");

    private static string WasmTs => Read("src", "Rask.Wasm", "Resources", "rask.wasm.ts");

    [Fact]
    public void The_gate_tracks_head_assets_and_waits_for_the_scoped_namespace()
    {
        var ts = GateTs;

        Assert.Contains("pendingHeadAssets", ts, StringComparison.Ordinal);
        Assert.Contains("function trackHeadAsset", ts, StringComparison.Ordinal);
        Assert.Contains("function namespaceReady", ts, StringComparison.Ordinal);
        Assert.Contains("function ensureNamespacePoll", ts, StringComparison.Ordinal);
        Assert.Contains("\"Rask.\"", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server", "function dispatchJsInvoke")]
    [InlineData("wasm", "function beginInvokeJS")]
    public void Each_runtime_parks_its_invokes_through_the_shared_gate(string runtime, string dispatcher)
    {
        var ts = runtime == "server" ? ServerTs : WasmTs;

        var start = ts.IndexOf(dispatcher, StringComparison.Ordinal);

        Assert.Contains("from \"../../Rask.Core/Resources/rask-head-assets.js\"", ts, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{dispatcher} not found in the {runtime} runtime");
        Assert.Contains("invokeGate.park(", ts.Substring(start, Math.Min(900, ts.Length - start)), StringComparison.Ordinal);
        Assert.DoesNotContain("function trackHeadAsset", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public void Each_runtime_drains_the_gate_after_a_diff_adds_head_assets(string runtime)
    {
        var ts = runtime == "server" ? ServerTs : WasmTs;

        Assert.Contains("invokeGate.scanHeadAssets();\n", ts, StringComparison.Ordinal);
        Assert.Contains("invokeGate.drain();", ts, StringComparison.Ordinal);
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
