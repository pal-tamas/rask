using System.Text.Json;
using Rask.TestSupport;

namespace Rask.External.Tests;

/// <summary>
///     The Angular island below the C# boundary: the shipped runtime, the shipped adapter, the real
///     <c>@angular/core</c> and <c>@angular/platform-browser</c> with JIT-compiled components, and a real DOM.
/// </summary>
/// <remarks>
///     <para>
///         The largest adapter and the only one whose bootstrap is asynchronous, which is where its two
///         promises in <c>docs/islands.md</c> come from: props that arrive while the application is still
///         being created are the ones rendered, and an island removed in that window destroys the
///         application when it appears.
///     </para>
///     <para>
///         <b>Not a substitute for a bundle.</b> Components here are JIT-compiled; an app's are AOT-compiled
///         by <c>@analogjs/vite-plugin-angular</c>, which <c>A_scaffolded_island_bundles</c> covers.
///     </para>
///     <para>Skips rather than fails when the fixture was not bundled — see the target in the .csproj.</para>
/// </remarks>
public sealed class AngularAdapterTests
{
    private const string Fixture = "AngularAdapterFixture";

    [Fact]
    public void An_angular_island_mounts_with_the_props_csharp_rendered()
    {
        var doc = Run();

        var heading = doc.GetProperty("headingOnMount").GetString();

        Assert.Equal("Revenue", heading);
        Assert.Equal(1, doc.GetProperty("createdOnMount").GetInt32());
        Assert.Equal("", doc.GetProperty("slotWithoutChildren").GetString());
        Assert.Empty(doc.GetProperty("bootstrapErrors").EnumerateArray());
    }

    [Fact]
    public void An_angular_prop_change_repaints_and_keeps_the_component_instance()
    {
        // Props are written from outside Angular, through setInput — assigning to the instance would update the
        // field and never repaint. An adapter that re-created the component to force the paint would lose the count.
        var doc = Run();

        var count = (doc.GetProperty("countAfterClick").GetString(), doc.GetProperty("countAfterUpdate").GetString());

        Assert.Equal(("1", "1"), count);
        Assert.Equal("Costs", doc.GetProperty("headingAfterUpdate").GetString());
        Assert.Equal(1, doc.GetProperty("createdAfterUpdate").GetInt32());
        Assert.Equal(0, doc.GetProperty("destroyedAfterUpdate").GetInt32());
    }

    [Fact]
    public void An_angular_output_bound_as_an_at_prop_reaches_the_host_dispatch_channel()
    {
        var doc = Run();

        var dispatched = Assert.Single(doc.GetProperty("dispatched").EnumerateArray());

        Assert.Equal("c7:3", dispatched.GetProperty("id").GetString());
        Assert.Equal("external", dispatched.GetProperty("type").GetString());
        Assert.Equal(42, dispatched.GetProperty("args")[0].GetInt32());
    }

    [Fact]
    public void An_angular_prop_csharp_stops_sending_unsubscribes_the_output_and_restores_the_inputs_default()
    {
        var doc = Run();

        var afterCall = doc.GetProperty("dispatchedAfterCall").GetInt32();
        var afterClear = doc.GetProperty("dispatchedAfterClear").GetInt32();

        Assert.Equal(afterCall, afterClear);
        Assert.Equal("untitled", doc.GetProperty("headingAfterClear").GetString());
    }

    [Fact]
    public void An_angular_islands_children_are_projected_into_its_ng_content()
    {
        // Projected nodes are fixed when a component is created, so the adapter projects ONE container up front and
        // reconciles inside it. Children that arrive after the first render must still land in the slot, without the
        // component being re-created to re-project them.
        var doc = Run();

        var slot = doc.GetProperty("slotWithChild").GetString();

        Assert.Equal("Revenue new:0", slot);
        Assert.True(doc.GetProperty("badgeInsideSlot").GetBoolean(), "the child island rendered outside the parent's <ng-content>");
        Assert.Equal(1, doc.GetProperty("createdAfterChildren").GetInt32());
        Assert.Equal(1, doc.GetProperty("childRequests").GetInt32());
    }

    [Fact]
    public void An_angular_child_island_keeps_its_state_across_a_parent_update_and_is_destroyed_when_removed()
    {
        var doc = Run();

        var badge = (doc.GetProperty("badgeAfterClick").GetString(), doc.GetProperty("badgeAfterParentUpdate").GetString());

        Assert.Equal(("new:1", "newer:1"), badge);
        Assert.Equal(1, doc.GetProperty("badgesCreatedAfterParentUpdate").GetInt32());
        Assert.Equal("1", doc.GetProperty("countAfterParentUpdate").GetString());
        Assert.True(doc.GetProperty("badgeGone").GetBoolean(), "the removed Angular child is still in the DOM");
        Assert.Equal(1, doc.GetProperty("badgesDestroyedAfterRemoval").GetInt32());
        Assert.Equal("Margin", doc.GetProperty("headingWithoutChild").GetString());
    }

    [Fact]
    public void Unmounting_an_angular_island_destroys_the_component_and_its_application_and_empties_it()
    {
        // Destroying an Angular view does not remove its root node, which is why the adapter renders into a child it
        // owns: bootstrapped straight into the host, the whole rendered tree would be left behind.
        var doc = Run();

        var destroyed = doc.GetProperty("destroyedAfterUnmount").GetInt32();

        Assert.Equal(1, destroyed);
        Assert.Equal(1, doc.GetProperty("appsDestroyedByUnmount").GetInt32());
        Assert.True(
            doc.GetProperty("islandEmptyAfterUnmount").GetBoolean(),
            "the island still had children after unmount, so the adapter left Angular's root node behind");
    }

    [Fact]
    public void An_angular_island_applies_props_once_its_bootstrap_resolves()
    {
        // Two updates land in the turn mount() was called in, before createApplication() can have resolved. Applying
        // the mount-time props on arrival would render the island two states behind with nothing left to correct it.
        var doc = Run();

        var heading = doc.GetProperty("headingAfterEarlyUpdate").GetString();

        Assert.False(doc.GetProperty("renderedBeforeBootstrap").GetBoolean(), "the bootstrap was synchronous, so this window was never open");
        Assert.Equal("Third", heading);
        Assert.Equal("late child", doc.GetProperty("slotAfterEarlyUpdate").GetString());
    }

    [Fact]
    public void An_angular_island_removed_mid_bootstrap_is_destroyed()
    {
        // Unmounted in the turn it was mounted in. The application does not exist yet, so there is nothing to destroy
        // THEN — the adapter has to destroy it when it is handed over, or its change detection runs against a
        // detached element for the life of the page.
        var doc = Run();

        var destroyedOnArrival = doc.GetProperty("appsDestroyedForAbandoned").GetInt32();

        Assert.Equal(0, doc.GetProperty("appsDestroyedSameTurn").GetInt32());
        Assert.Equal(1, destroyedOnArrival);
        Assert.Equal(0, doc.GetProperty("componentsCreatedForAbandoned").GetInt32());
        Assert.True(doc.GetProperty("abandonedIsEmpty").GetBoolean(), "the abandoned island was left with the adapter's node in it");
    }

    /// <summary>Runs the fixture, or skips with the reason it could not.</summary>
    private static JsonElement Run()
    {
        Assert.SkipUnless(
            File.Exists(NodeFixture.ScriptPath(Fixture)),
            $"'{NodeFixture.ScriptPath(Fixture)}' was not bundled: npm could not install the adapter fixtures (no npm, no "
            + "network, or RaskPreactFixture=false), so the Angular adapter was not exercised on this machine.");

        var doc = NodeFixture.Run(Fixture);
        Assert.SkipWhen(doc is null, "node is not on PATH, so the Angular adapter was not exercised.");

        return doc!.Value;
    }
}
