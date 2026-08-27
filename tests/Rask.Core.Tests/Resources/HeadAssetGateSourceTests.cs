namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the head-asset gate in the Server client runtime.
/// </summary>
/// <remarks>
///     <para>
///         A <c>Rask.*</c> invoke must not dispatch until the assets a component declared in
///         <c>Head</c> have loaded. Without the gate, a <c>CodeSample</c>-shaped component whose
///         <c>Head</c> pulls in a CDN script would have to hand-roll a load listener of its own, and
///         a first-render <c>OnRenderedAsync</c> would fail with "Could not find … on target".
///     </para>
///     <para>
///         <b>Why here and not against the served script.</b> Every name below is a local binding,
///         and Release now minifies the served runtime for real — <c>headAssetsReady</c> is a single
///         letter in the shipped bytes. <c>RuntimeScriptEndpointTests</c> used to assert these
///         against the response body and could, because "minification" then meant a comment stripper
///         that renamed nothing. Against a real minifier the same assertions pass or fail on which
///         configuration was built last. That test now asks the served script only what survives
///         minification; the structure is asserted here, where it is legible.
///     </para>
/// </remarks>
public class HeadAssetGateSourceTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ServerTs => Read("src", "Rask.Server", "Resources", "rask.ts");

    [Fact]
    public void The_gate_primitives_are_present()
    {
        var ts = ServerTs;

        Assert.Contains("pendingHeadAssets", ts, StringComparison.Ordinal);
        Assert.Contains("function trackHeadAsset", ts, StringComparison.Ordinal);
        Assert.Contains("function scanHeadAssets", ts, StringComparison.Ordinal);
        Assert.Contains("function headAssetsReady", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dispatcher_consults_headAssetsReady_not_just_scopedJsReady()
    {
        var ts = ServerTs;

        // Both conditions, in the prologue: an invoke parks unless the scoped bundle is in AND every
        // Head-declared asset has settled. Checking only scopedJsReady is the original bug.
        var dispatch = ts.IndexOf("function dispatchJsInvoke", StringComparison.Ordinal);
        Assert.True(dispatch >= 0, "dispatchJsInvoke not found in rask.ts");

        var prelude = ts.Substring(dispatch, Math.Min(900, ts.Length - dispatch));
        Assert.Contains("headAssetsReady()", prelude, StringComparison.Ordinal);
        Assert.Contains("scopedJsReady", prelude, StringComparison.Ordinal);
        Assert.Contains("pendingScopedInvokes.push", prelude, StringComparison.Ordinal);
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
