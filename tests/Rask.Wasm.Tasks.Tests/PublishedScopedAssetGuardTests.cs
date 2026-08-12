namespace Rask.Wasm.Tasks.Tests;

/// <summary>
///     Pins the publish-time guard in <c>Rask.Wasm.targets</c> that fails a WASM publish whose scoped
///     assets were baked but never reached the published output.
/// </summary>
/// <remarks>
///     The bake stages into <c>obj/…/rask-scoped</c> and registers computed static web assets in the build
///     pass, trusting them to flow into the publish manifest. When that link breaks — most reliably by
///     building the project in both <c>WasmBuildNative</c> modes through one <c>obj/</c> — the published
///     bundle simply has no <c>/_rask/a/</c>, and nothing says so: the app builds, publishes, boots and
///     renders, with only its scoped CSS/JS missing. For an app whose scoped JS owns something
///     load-bearing that presents as a hung page, which is how #650/#652 cost two people a debugging
///     session each.
///     <para>
///         There is no harness here for executing targets, so this asserts the guard's <i>shape</i>: that
///         it still runs after publish, still compares staged against published rather than merely
///         checking one of them, and is still scoped to Rask WASM projects. The behaviour itself was
///         verified by running the target against a real publish in all three states — staged-but-not-
///         published (errors), neither (passes), both (passes).
///     </para>
/// </remarks>
public sealed class PublishedScopedAssetGuardTests
{
    private static readonly string _targets = File.ReadAllText(Path.Combine(
        LocateRepoRoot(), "src", "Rask.Wasm", "build", "Rask.Wasm.targets"));

    [Fact]
    public void The_publish_is_verified_after_it_runs_for_rask_wasm_projects()
    {
        Assert.Contains("_RaskVerifyPublishedScopedAssets", _targets, StringComparison.Ordinal);

        var target = TargetBody();
        Assert.Contains("AfterTargets=\"Publish\"", target, StringComparison.Ordinal);
        Assert.Contains("'$(RaskWasm)' == 'true'", target, StringComparison.Ordinal);
    }

    // The comparison is the whole point. A guard that only checked the published side would fail every
    // project that legitimately has no scoped assets; one that only checked staging would never fire.
    [Fact]
    public void The_guard_compares_what_was_staged_against_what_shipped()
    {
        var target = TargetBody();

        Assert.Contains("_RaskStagedScopedAsset", target, StringComparison.Ordinal);
        Assert.Contains("_RaskPublishedScopedAsset", target, StringComparison.Ordinal);
        // Staged side reads the bake's staging dir; published side reads the publish output.
        Assert.Contains("$(_RaskVerifyStageDir)_rask\\a\\**\\*", target, StringComparison.Ordinal);
        Assert.Contains("$(PublishDir)wwwroot\\_rask\\a\\**\\*", target, StringComparison.Ordinal);
        Assert.Contains("rask-scoped", _targets, StringComparison.Ordinal);

        // Errors only on staged-non-empty AND published-empty — both halves, or it misfires.
        Assert.Contains("'@(_RaskStagedScopedAsset)' != '' AND '@(_RaskPublishedScopedAsset)' == ''",
            target, StringComparison.Ordinal);
    }

    // The message is the deliverable: the failure it replaces named nothing at all.
    [Fact]
    public void The_error_says_what_broke_and_what_to_do()
    {
        var target = TargetBody();

        Assert.Contains("<Error", target, StringComparison.Ordinal);
        Assert.Contains("404", target, StringComparison.Ordinal);
        Assert.Contains("WasmBuildNative", target, StringComparison.Ordinal);
    }

    // The second guard, and the one that catches what actually happened in #650: the bake never ran, so
    // the staging dir is absent and the staged-vs-published comparison sees nothing to compare. Without
    // _RaskScopedBakeRan, "this project has no scoped assets" and "the bake was skipped" are the same
    // observation — and the first guard stays silent through the second.
    [Fact]
    public void A_bake_that_never_ran_is_distinguished_from_a_project_with_nothing_to_bake()
    {
        Assert.Contains("<_RaskScopedBakeRan>true</_RaskScopedBakeRan>", _targets, StringComparison.Ordinal);

        var target = TargetBody();
        Assert.Contains("'$(_RaskScopedBakeRan)' != 'true' AND '@(_RaskPublishedScopedAsset)' == ''",
            target, StringComparison.Ordinal);

        // The published half is not optional: an incremental publish can skip the build pass (and so the
        // bake) while the assets already sit correctly in wwwroot. Requiring both keeps that quiet.
        Assert.Contains("_RaskBakeScopedStaticWebAssets", target, StringComparison.Ordinal);
    }

    private static string TargetBody()
    {
        const string open = "<Target Name=\"_RaskVerifyPublishedScopedAssets\"";
        var start = _targets.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "The _RaskVerifyPublishedScopedAssets target is gone from Rask.Wasm.targets.");

        var end = _targets.IndexOf("</Target>", start, StringComparison.Ordinal);
        Assert.True(end > start, "The _RaskVerifyPublishedScopedAssets target is not closed.");
        return _targets[start..end];
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
