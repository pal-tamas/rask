using System.Text.Json;
using Rask.TestSupport;

namespace Rask.External.Tests;

/// <summary>
///     The Preact island, driven end to end below the C# boundary: the shipped runtime, the shipped
///     adapter, the real preact, and a real DOM.
/// </summary>
/// <remarks>
///     <para>
///         Preact was the one runtime of the seven with no sample and no coverage past the C# boundary
///         (<see href="https://github.com/pal-tamas/rask/issues/963" />), because it cannot share a
///         project with React — <c>@vitejs/plugin-react</c> resolves Babel 8 while
///         <c>@preact/preset-vite</c> pins a <c>@babel/core@"7.x"</c> peer, so npm refuses the pair, and
///         both showcases already carry a React island. That refusal is correct and stays. It constrains
///         a bundled APP; a fixture installs preact and nothing else, so it never arises here.
///     </para>
///     <para>
///         <b>What this covers that <see cref="ExternalRuntimeTests" /> does not.</b> That suite drives
///         the same runtime against a FAKE adapter, which is the right way to pin the runtime's own
///         sequencing and says nothing about any real framework binding. Here the adapter is real, so a
///         change to <c>src/Rask.External/client/preact.ts</c> that still satisfies the
///         <c>ExternalAdapter</c> shape — returning a fresh element as the handle, rendering into a child
///         instead of the island, dropping the element on unmount instead of calling
///         <c>render(null, …)</c> — fails here and nowhere else.
///     </para>
///     <para>
///         <b>Not a substitute for a browser.</b> Nothing here builds a Preact island through Vite, so
///         <c>@preact/preset-vite</c>'s transform is still uncovered; that needs a real bundle and a real
///         page. What is covered is the adapter contract and preact's own behaviour under it, which is
///         where all four of the bugs #958 found in the sibling adapters lived.
///     </para>
///     <para>
///         Skips rather than fails when the fixture was not bundled — see the target in the .csproj.
///     </para>
/// </remarks>
public sealed class PreactAdapterTests
{
    private const string Fixture = "PreactAdapterFixture";

    [SkippableFact]
    public void The_island_mounts_with_the_props_csharp_rendered()
    {
        var doc = Run();

        Assert.Equal(
            new[] { "Chart" },
            doc.GetProperty("requested").EnumerateArray().Select(e => e.GetString()).ToArray());

        // Rendered by preact into the island element itself. The adapter's handle IS that element, and
        // an adapter that mounted into a child of it would strand every later update on the wrong node.
        Assert.Equal("Revenue", doc.GetProperty("headingOnMount").GetString());

        // The component's own effect ran once, which is what "mounted" means to anything with a
        // subscription in it.
        Assert.Equal(1, doc.GetProperty("effectsOnMount").GetInt32());
    }

    [SkippableFact]
    public void A_prop_change_reconciles_and_the_components_own_state_survives()
    {
        // The single most important behaviour in the adapter, and the one no C# test can see. Preact's
        // update path is `render()` into the SAME element — it diffs against the tree already there.
        // An adapter that mounted somewhere new, or emptied the element first, would still pass every
        // shape check and would throw away the component's state on a prop change the user never asked
        // to be destructive: scroll position, focus, an open dropdown, a half-typed field.
        var doc = Run();

        // Moved off its initial value by a click, and known to nothing on the C# side.
        Assert.Equal("1", doc.GetProperty("countAfterClick").GetString());

        Assert.Equal("Costs", doc.GetProperty("headingAfterUpdate").GetString());
        Assert.Equal("1", doc.GetProperty("countAfterUpdate").GetString());

        // A remount would show up here too: the mount effect re-running, and its cleanup with it.
        Assert.Equal(1, doc.GetProperty("effectsAfterUpdate").GetInt32());
        Assert.Equal(0, doc.GetProperty("cleanupsAfterUpdate").GetInt32());
    }

    [SkippableFact]
    public void A_callback_called_inside_the_component_reaches_the_host_dispatch_channel()
    {
        var doc = Run();

        Assert.Equal(1, doc.GetProperty("dispatchedAfterCall").GetInt32());

        var dispatched = Assert.Single(doc.GetProperty("dispatched").EnumerateArray());

        // The same channel every DOM handler uses — the open socket on the Server, a direct JSExport
        // call on WASM — rather than one the island opened for itself.
        Assert.Equal("c7:3", dispatched.GetProperty("id").GetString());
        Assert.Equal("external", dispatched.GetProperty("type").GetString());
        Assert.Equal(42, dispatched.GetProperty("args")[0].GetInt32());
    }

    [SkippableFact]
    public void A_callback_cleared_in_csharp_stops_firing()
    {
        // C# stopped passing the delegate, so the prop is simply absent from the next props JSON. The
        // component must see that: a render that reused the previous props, or a handler cache that
        // outlived the prop, would keep dispatching to a callback the server no longer has.
        var doc = Run();

        Assert.Equal(
            doc.GetProperty("dispatchedAfterCall").GetInt32(),
            doc.GetProperty("dispatchedAfterClear").GetInt32());
    }

    [SkippableFact]
    public void Unmount_runs_the_components_cleanup_effects()
    {
        // `render(null, element)` is preact's unmount. Dropping the element instead looks identical in
        // the DOM — the island is going away regardless — and leaks every effect still subscribed
        // inside it: timers, listeners, an open EventSource, for the life of the page.
        var doc = Run();

        Assert.Equal(1, doc.GetProperty("cleanupsAfterUnmount").GetInt32());
        Assert.True(
            doc.GetProperty("islandEmptyAfterUnmount").GetBoolean(),
            "the island still had children after unmount, so preact never emptied it");
    }

    [SkippableFact]
    public void A_child_island_renders_inside_its_parent_and_keeps_its_own_state_across_a_parent_update()
    {
        // Children are rendered by Preact inside the parent's own tree, so a parent re-render reconciles them like any
        // other child: the same component at the same key keeps its instance. A child re-created per update would show
        // its click count back at 0 and its mount effect running twice.
        var doc = Run();

        Assert.Equal("Revenue new:0", doc.GetProperty("slotWithChild").GetString());
        Assert.Equal("new:1", doc.GetProperty("badgeAfterClick").GetString());
        Assert.Equal("newer:1", doc.GetProperty("badgeAfterParentUpdate").GetString());
        Assert.Equal(1, doc.GetProperty("badgeEffectsAfterParentUpdate").GetInt32());

        // The parent's own state is untouched by its children changing, and the child's chunk was fetched once.
        Assert.Equal("1", doc.GetProperty("countAfterChildUpdate").GetString());
        Assert.Equal(1, doc.GetProperty("childRequests").GetInt32());
    }

    [SkippableFact]
    public void A_child_island_csharp_removes_unmounts_with_its_cleanup_and_the_parent_stays()
    {
        var doc = Run();

        Assert.True(doc.GetProperty("badgeGone").GetBoolean(), "the removed child is still in the DOM");
        Assert.Equal(1, doc.GetProperty("badgeCleanupsAfterRemoval").GetInt32());
        Assert.Equal("Margin", doc.GetProperty("headingWithoutChild").GetString());
    }

    /// <summary>Runs the fixture, or skips with the reason it could not.</summary>
    private static JsonElement Run()
    {
        Skip.IfNot(
            File.Exists(NodeFixture.ScriptPath(Fixture)),
            $"'{NodeFixture.ScriptPath(Fixture)}' was not bundled: npm could not install preact and "
            + "happy-dom for the fixture (no npm, no network, or RaskPreactFixture=false), so the Preact "
            + "adapter was not exercised on this machine.");

        var doc = NodeFixture.Run(Fixture);
        Skip.If(doc is null, "node is not on PATH, so the Preact adapter was not exercised.");

        return doc!.Value;
    }
}
