using System.Text.Json;
using Rask.TestSupport;

namespace Rask.External.Tests;

/// <summary>
///     The Svelte island below the C# boundary: the shipped runtime, the shipped adapter and its two host
///     components as Svelte's own compiler builds them, the real svelte, and a real DOM.
/// </summary>
/// <remarks>
///     Skips rather than fails when the fixture was not bundled — it needs the npm install the sibling
///     adapter fixtures share, and node at build time to run the Svelte compiler. See the target in the .csproj.
/// </remarks>
public sealed class SvelteAdapterTests
{
    private const string Fixture = "SvelteAdapterFixture";

    [Fact]
    public void A_svelte_island_mounts_with_the_props_csharp_rendered()
    {
        var doc = Run();

        var heading = doc.GetProperty("headingOnMount").GetString();

        Assert.Equal("Revenue", heading);
        Assert.Equal(1, doc.GetProperty("effectsOnMount").GetInt32());
        Assert.Equal("", doc.GetProperty("slotWithoutChildren").GetString());
    }

    [Fact]
    public void A_svelte_prop_change_keeps_the_components_own_state()
    {
        // The reason the adapter is a `.svelte.ts` module at all: new props are written into the same `$state` object,
        // so Svelte patches what changed. Without the rune the only way to show new props is to remount, and that
        // throws away the component's own state on every C# re-render.
        var doc = Run();

        var count = (doc.GetProperty("countAfterClick").GetString(), doc.GetProperty("countAfterUpdate").GetString());

        Assert.Equal(("1", "1"), count);
        Assert.Equal("Costs", doc.GetProperty("headingAfterUpdate").GetString());
        Assert.Equal(1, doc.GetProperty("effectsAfterUpdate").GetInt32());
        Assert.Equal(0, doc.GetProperty("cleanupsAfterUpdate").GetInt32());
    }

    [Fact]
    public void A_svelte_callback_prop_reaches_the_host_dispatch_channel()
    {
        var doc = Run();

        var dispatched = Assert.Single(doc.GetProperty("dispatched").EnumerateArray());

        Assert.Equal("c7:3", dispatched.GetProperty("id").GetString());
        Assert.Equal("external", dispatched.GetProperty("type").GetString());
        Assert.Equal(42, dispatched.GetProperty("args")[0].GetInt32());
    }

    [Fact]
    public void A_svelte_prop_csharp_stops_sending_is_removed_from_the_component()
    {
        // An unwired callback and an unset prop are simply absent from the next props JSON. Assigning the new props
        // over the old ones would leave both behind: the stale callback still firing, the old heading still showing.
        var doc = Run();

        var afterCall = doc.GetProperty("dispatchedAfterCall").GetInt32();
        var afterClear = doc.GetProperty("dispatchedAfterClear").GetInt32();

        Assert.Equal(afterCall, afterClear);
        Assert.Equal("untitled", doc.GetProperty("headingAfterClear").GetString());
    }

    [Fact]
    public void A_svelte_islands_children_render_as_its_children_snippet_without_remounting_it()
    {
        // The children arrive as an explicit `children` prop, not as content: content would need an {#if} around the
        // component, and switching branches when the first child appears would re-create it.
        var doc = Run();

        var slot = doc.GetProperty("slotWithChild").GetString();

        Assert.Equal("Revenue new:0", slot);
        Assert.Equal("1", doc.GetProperty("countAfterChildrenArrive").GetString());
        Assert.Equal(1, doc.GetProperty("effectsAfterChildrenArrive").GetInt32());
        Assert.Equal(1, doc.GetProperty("childRequests").GetInt32());
    }

    [Fact]
    public void A_svelte_child_island_keeps_its_state_across_a_parent_update_and_unmounts_when_removed()
    {
        var doc = Run();

        var badge = (doc.GetProperty("badgeAfterClick").GetString(), doc.GetProperty("badgeAfterParentUpdate").GetString());

        Assert.Equal(("new:1", "newer:1"), badge);
        Assert.Equal(1, doc.GetProperty("badgeEffectsAfterParentUpdate").GetInt32());
        Assert.Equal("1", doc.GetProperty("countAfterParentUpdate").GetString());
        Assert.True(doc.GetProperty("badgeGone").GetBoolean(), "the removed Svelte child is still in the DOM");
        Assert.Equal(1, doc.GetProperty("badgeCleanupsAfterRemoval").GetInt32());
        Assert.Equal("Margin", doc.GetProperty("headingWithoutChild").GetString());
        Assert.Equal(1, doc.GetProperty("effectsAfterChildRemoval").GetInt32());
    }

    [Fact]
    public void Unmounting_a_svelte_island_runs_its_teardown_and_empties_it()
    {
        var doc = Run();

        var cleanups = doc.GetProperty("cleanupsAfterUnmount").GetInt32();

        Assert.Equal(1, cleanups);
        Assert.True(
            doc.GetProperty("islandEmptyAfterUnmount").GetBoolean(),
            "the island still had children after unmount, so svelte never removed what it mounted");
    }

    /// <summary>Runs the fixture, or skips with the reason it could not.</summary>
    private static JsonElement Run()
    {
        Assert.SkipUnless(
            File.Exists(NodeFixture.ScriptPath(Fixture)),
            $"'{NodeFixture.ScriptPath(Fixture)}' was not bundled: npm could not install the adapter fixtures (no npm, no "
            + "network, or RaskPreactFixture=false) or node was not on PATH to run the Svelte compiler, so the Svelte "
            + "adapter was not exercised on this machine.");

        var doc = NodeFixture.Run(Fixture);
        Assert.SkipWhen(doc is null, "node is not on PATH, so the Svelte adapter was not exercised.");

        return doc!.Value;
    }
}
