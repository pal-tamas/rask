using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class RuntimeScriptInjectionTests : global::Rask.Core.RaskMarkup
{
    private const string ScriptHtml = "<script src=\"/rask/rask.js\"></script>";

    private static ServiceProvider WithProvider() =>
        new ServiceCollection()
            .AddSingleton<IRaskRuntimeScript>(new StubRuntimeScriptProvider(Raw.Value(ScriptHtml)))
            .BuildServiceProvider();

    private static Component Shell(params Component[] bodyChildren) =>
        [Doctype, Html.Lang("en")[Head, Body[bodyChildren]]];

    [Fact]
    public void Body_ProviderRegistered_InjectsScriptAsLastBodyChild()
    {
        var view = new StubComponent(() => Shell(P["hi"]));

        var html = view.RenderAsLiveRoot(WithProvider());

        // Script is auto-injected even though the tree never mentions RaskRuntimeScript().
        Assert.Contains(ScriptHtml + "</body>", html);
    }

    [Fact]
    public void Body_NoProvider_InjectsNothing()
    {
        var view = new StubComponent(() => Shell(P["hi"]));

        var html = view.RenderAsLiveRoot(RenderHarness.EmptyServices());

        Assert.DoesNotContain("rask.js", html);
        Assert.Contains("<p>hi</p></body>", html);
    }

    [Fact]
    public void Body_LegacyRaskRuntimeScriptStillInTree_EmitsExactlyOneScript()
    {
        // RaskRuntimeScript() is a no-op; the framework injects one script at body close.
        var view = new StubComponent(() => Shell(P["hi"], RaskRuntimeScript));

        var html = view.RenderAsLiveRoot(WithProvider());

        var first = html.IndexOf(ScriptHtml, StringComparison.Ordinal);
        Assert.True(first >= 0, "expected the injected runtime script");
        Assert.Equal(-1, html.IndexOf(ScriptHtml, first + ScriptHtml.Length, StringComparison.Ordinal));
    }

    [Fact]
    public void NonLiveToHtml_DoesNotInject()
    {
        // Body().ToHtml() outside a live render must stay bare (no provider reachable anyway).
        Assert.Equal("<body></body>", Body.ToHtml());
    }

    // ---- The host injections must not leave an entry slot behind -------------------------------
    //
    // Both host contributions (this one and IRaskHeadContribution) are built during SERIALIZATION, so
    // the entry slot a CHAIN pushes is attributed to whichever component is on the parent stack — and
    // that component's Render() returned long ago, taking its drain with it. Nothing pops the slot, so
    // it stays on a [ThreadStatic] list holding a Component from a finished page, and through its
    // LiveState the whole live session.
    //
    // Note the stub the tests above use returns a PRE-BUILT component, which pushes no slot and so
    // cannot see any of this. The stub below builds through the chain, the way the real
    // ServerRuntimeScript does (`Script.Src(...)`) — which is the only shape that reproduces it.

    [Fact]
    public void Body_ProviderBuildsThroughTheChain_LeavesNoEntrySlotBehind()
    {
        var view = new StubComponent(() => Shell(P["hi"]));
        using var provider = new ServiceCollection()
            .AddSingleton<IRaskRuntimeScript>(new ChainRuntimeScriptProvider())
            .BuildServiceProvider();

        var before = BuilderRuntime.SlotDepth;
        var html = view.RenderAsLiveRoot(provider);

        Assert.Contains("<script src=\"/rask/rask.js\"></script></body>", html);
        Assert.Equal(before, BuilderRuntime.SlotDepth);
    }

    [Fact]
    public void Head_ProviderBuildsThroughTheChain_LeavesNoEntrySlotBehind()
    {
        var view = new StubComponent(() => Shell(P["hi"]));
        using var provider = new ServiceCollection()
            .AddSingleton<IRaskHeadContribution>(new ChainHeadContribution())
            .BuildServiceProvider();

        var before = BuilderRuntime.SlotDepth;
        view.RenderAsLiveRoot(provider);

        Assert.Equal(before, BuilderRuntime.SlotDepth);
    }

    [Fact]
    public void A_rendered_page_is_collectible_once_the_render_has_returned()
    {
        // The symptom, not the bookkeeping: a leaked slot holds the rendered COMPONENT, and on a real
        // host the rendering thread is a long-lived request worker — so every page served on it was
        // retained for the life of the process (~1.1 MB per live session, session-churn, #922).
        //
        // Rendered inside a non-inlined method so the only thing crossing back is the WeakReference:
        // a local still in scope would keep the tree alive on its own and the test would fail for a
        // reason that is not the bug. Deliberately NOT rendered on a throwaway thread — the leaked
        // slot list is [ThreadStatic], so a thread that exits takes the leak with it and the test
        // would pass while the bug was live.
        var weak = RenderAndDrop();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.TryGetTarget(out _), "the rendered page was still reachable after its render returned");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Component> RenderAndDrop()
    {
        var view = new StubComponent(() => Shell(P["hi"]));
        using var provider = new ServiceCollection()
            .AddSingleton<IRaskRuntimeScript>(new ChainRuntimeScriptProvider())
            .BuildServiceProvider();

        view.RenderAsLiveRoot(provider);
        return new WeakReference<Component>(view);
    }

    [Fact]
    public void A_prop_the_contribution_stops_naming_is_reset_before_it_is_serialized()
    {
        // ORDER, not just presence. The drain is the reset the enclosing Render() would have run, and
        // Component runs it BEFORE the serializer walks the child. Draining afterwards would serialize
        // the PREVIOUS render's value for any prop this render's chain did not name — and on the server
        // that stale byte is diffed and pushed to the client, breaking the byte-stability the injection
        // relies on.
        var contribution = new ConditionalHeadContribution { Color = "#000" };
        var view = new StubComponent(() => Shell(P["hi"]));
        using var provider = new ServiceCollection()
            .AddSingleton<IRaskHeadContribution>(contribution)
            .BuildServiceProvider();

        Assert.Contains("content=\"#000\"", view.RenderAsLiveRoot(provider));

        contribution.Color = null; // the chain stops naming Content
        var second = view.RenderAsLiveRoot(provider);

        Assert.DoesNotContain("content=\"#000\"", second);
    }

    // Names Content only sometimes, which is what makes the reset observable.
    private sealed partial class ConditionalHeadContribution : global::Rask.Core.RaskMarkup, IRaskHeadContribution
    {
        public string? Color { get; set; }

        public Component Render() =>
            Color is null ? Meta.Name("theme-color") : Meta.Name("theme-color").Content(Color);
    }

    [Fact]
    public void A_host_contribution_that_throws_still_drains_its_entry_slot()
    {
        // The error path leaks too, and it is the one a straight-line drain misses: the fault unwinds
        // past the drain and the slot stays, once per fault, on a thread that outlives the request.
        // A contribution CAN throw — it is host-supplied code running inside the serializer.
        var view = new StubComponent(() => Shell(P["hi"]));
        using var provider = new ServiceCollection()
            .AddSingleton<IRaskRuntimeScript>(new ThrowingChainRuntimeScriptProvider())
            .BuildServiceProvider();

        var before = BuilderRuntime.SlotDepth;

        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot(provider));
        Assert.Equal(before, BuilderRuntime.SlotDepth);
    }

    // Pushes a slot through the chain, then throws while the serializer is still inside the bracket.
    private sealed partial class ThrowingChainRuntimeScriptProvider
        : global::Rask.Core.RaskMarkup, IRaskRuntimeScript
    {
        public Component Render()
        {
            _ = Script.Src("/rask/rask.js");
            throw new InvalidOperationException("boom");
        }
    }

    // Builds through the chain rather than handing back a ready-made component — see the note above.
    private sealed partial class ChainRuntimeScriptProvider : global::Rask.Core.RaskMarkup, IRaskRuntimeScript
    {
        public Component Render() => Script.Src("/rask/rask.js");
    }

    private sealed partial class ChainHeadContribution : global::Rask.Core.RaskMarkup, IRaskHeadContribution
    {
        public Component Render() => Meta.Name("theme-color").Content("#000");
    }

    private sealed class StubRuntimeScriptProvider : IRaskRuntimeScript
    {
        private readonly Component _component;
        public StubRuntimeScriptProvider(Component component) => _component = component;
        public Component Render() => _component;
    }
}
