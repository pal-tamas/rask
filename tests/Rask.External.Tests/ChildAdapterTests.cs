using System.Text.Json;
using Rask.TestSupport;

namespace Rask.External.Tests;

// Three adapters, one contract: each fixture runs the same children scenario against the REAL framework, so a
// difference between adapters shows up as the same assertion failing for one of them. Bundled from the shared fixture
// install (react, react-dom, vue, solid-js beside preact and happy-dom) — see the targets in the .csproj — and skipped,
// never failed, where that install could not happen.

/// <summary>The React adapter's children, against the real react and react-dom.</summary>
public sealed class ReactAdapterTests
{
    [SkippableFact]
    public void A_child_island_renders_inside_its_parent_keeps_its_state_and_unmounts_when_removed() =>
        ChildAdapterRun.AssertChildren("ReactAdapterFixture", "React", remounts: "badgeEffectsAfterParentUpdate");
}

/// <summary>The Vue adapter's children — the component's default slot — against the real vue runtime.</summary>
public sealed class VueAdapterTests
{
    [SkippableFact]
    public void A_child_island_renders_inside_its_parent_keeps_its_state_and_unmounts_when_removed() =>
        ChildAdapterRun.AssertChildren("VueAdapterFixture", "Vue", remounts: "badgeEffectsAfterParentUpdate");
}

/// <summary>The Solid adapter's children — a reconciled store — against the real solid-js browser build.</summary>
public sealed class SolidAdapterTests
{
    // Solid runs a component function once for its life, so the count of runs IS the remount check.
    [SkippableFact]
    public void A_child_island_renders_inside_its_parent_keeps_its_state_and_unmounts_when_removed() =>
        ChildAdapterRun.AssertChildren("SolidAdapterFixture", "Solid", remounts: "badgeRunsAfterParentUpdate");
}

internal static class ChildAdapterRun
{
    public static void AssertChildren(string fixture, string runtime, string remounts)
    {
        var doc = Run(fixture, runtime);

        // Rendered inside the parent's own tree, in its slot.
        Assert.Equal("Revenue new:0", doc.GetProperty("slotWithChild").GetString());
        Assert.Equal("new:1", doc.GetProperty("badgeAfterClick").GetString());

        // A parent update reconciles the child in place: its own state survives, its new prop arrives, it is never
        // created a second time — and the parent's own state is untouched.
        Assert.Equal("newer:1", doc.GetProperty("badgeAfterParentUpdate").GetString());
        Assert.Equal(1, doc.GetProperty(remounts).GetInt32());
        Assert.Equal("1", doc.GetProperty("countAfterParentUpdate").GetString());
        Assert.Equal(1, doc.GetProperty("effectRuns").GetInt32());

        // A child C# removes unmounts with its cleanup, and the parent stays.
        Assert.True(doc.GetProperty("badgeGone").GetBoolean(), $"the removed {runtime} child is still in the DOM");
        Assert.Equal(1, doc.GetProperty("badgeCleanupsAfterRemoval").GetInt32());
        Assert.Equal("Margin", doc.GetProperty("headingWithoutChild").GetString());

        Assert.Equal(1, doc.GetProperty("childRequests").GetInt32());
    }

    private static JsonElement Run(string fixture, string runtime)
    {
        Skip.IfNot(
            File.Exists(NodeFixture.ScriptPath(fixture)),
            $"'{NodeFixture.ScriptPath(fixture)}' was not bundled: npm could not install the adapter fixtures (no npm, no "
            + $"network, or RaskPreactFixture=false), so the {runtime} adapter's children were not exercised on this machine.");

        var doc = NodeFixture.Run(fixture);
        Skip.If(doc is null, $"node is not on PATH, so the {runtime} adapter's children were not exercised.");

        return doc!.Value;
    }
}
