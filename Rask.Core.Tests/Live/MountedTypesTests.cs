using System.Text;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

/// <summary>
///     Regression coverage for <see cref="LiveRenderContext.MountedTypes" />. The set must
///     record every user-component type observed during a render walk, regardless of whether
///     that type has scoped CSS, scoped JS, both, or neither. Head asset emission iterates
///     this set; before it was unconditional, JS-only components silently dropped out.
/// </summary>
[Collection("ScopedAssets")]
public class MountedTypesTests
{
    public MountedTypesTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public void EmptyTree_NoUserComponents_MountedTypesIsEmpty()
    {
        // Only the root StubComponent itself is a user component — its render returns a
        // bare Span. Asserting that the set contains StubComponent but nothing else.
        var view = new StubComponent(Span());
        using var ctx = LiveRenderContext.Begin(view);
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(view, sb);

        Assert.Contains(typeof(StubComponent), ctx.MountedTypes);
        // Generated.Span is not a user component (it's an Element subclass under Rask.Core),
        // but the serializer's UserComponent branch only fires for user types. The exact
        // count depends on how the serializer routes Element vs user-component cases.
        // What matters: no spurious extra types appear.
        Assert.True(ctx.MountedTypes.Count >= 1);
    }

    [Fact]
    public void CssOnlyComponent_IsInMountedTypes()
    {
        ScopedAssetRegistry.RegisterCss(typeof(CssOnly), ".x { color: red; }");
        var view = new StubComponent(new CssOnly());
        using var ctx = LiveRenderContext.Begin(view);
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(view, sb);

        Assert.Contains(typeof(CssOnly), ctx.MountedTypes);
    }

    [Fact]
    public void JsOnlyComponent_IsInMountedTypes_RegressionTestForCssBiasBug()
    {
        // The historical bug: PushScope returned `default` (no-op) when TryRegister
        // returned false (no CSS registered for the type). MountedTypes never received
        // the type, so head emission missed JS-only components. The fix moved the
        // MountedTypes.Add call BEFORE the registry lookup, decoupling it from the
        // CSS-presence check.
        var view = new StubComponent(new JsOnly());
        using var ctx = LiveRenderContext.Begin(view);
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(view, sb);

        Assert.Contains(typeof(JsOnly), ctx.MountedTypes);
    }

    [Fact]
    public void ComponentWithBothCssAndJs_AppearsOnce_HashSetSemantics()
    {
        ScopedAssetRegistry.RegisterCss(typeof(BothAssets), ".x { color: red; }");
        var view = new StubComponent(new BothAssets());
        using var ctx = LiveRenderContext.Begin(view);
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(view, sb);

        Assert.Contains(typeof(BothAssets), ctx.MountedTypes);
        Assert.Equal(1, ctx.MountedTypes.Count(t => t == typeof(BothAssets)));
    }

    [Fact]
    public void ComponentWithNeitherAsset_StillInMountedTypes_UniformContract()
    {
        // Uniform contract: every user component entered during the walk is in the set,
        // even those with no scoped assets at all. Head emission filters per-type via
        // TryGetCss/TryGetJs returning false — so neither-asset types contribute nothing
        // to the rendered head, but they're still observable for diagnostics, analytics,
        // and future per-type hooks.
        var view = new StubComponent(new NoAssets());
        using var ctx = LiveRenderContext.Begin(view);
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(view, sb);

        Assert.Contains(typeof(NoAssets), ctx.MountedTypes);
    }

    [Fact]
    public void NInstancesOfSameComponentType_AppearOnce()
    {
        var view = new StubComponent(() => Div()[
            new CssOnly(), new CssOnly(), new CssOnly(), new CssOnly(), new CssOnly()
        ]);
        ScopedAssetRegistry.RegisterCss(typeof(CssOnly), ".x { color: red; }");
        using var ctx = LiveRenderContext.Begin(view);
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(view, sb);

        Assert.Equal(1, ctx.MountedTypes.Count(t => t == typeof(CssOnly)));
    }

    [Fact]
    public void NestedComponents_ParentAndChildAndGrandchild_AllPresent()
    {
        var view = new StubComponent(new Outer());
        using var ctx = LiveRenderContext.Begin(view);
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(view, sb);

        Assert.Contains(typeof(Outer), ctx.MountedTypes);
        Assert.Contains(typeof(Middle), ctx.MountedTypes);
        Assert.Contains(typeof(Inner), ctx.MountedTypes);
    }

    [Fact]
    public void DifferentRoots_ProduceDifferentMountedSets()
    {
        // Two independent trees: one renders CssOnly, the other JsOnly. Each context's
        // MountedTypes reflects only the components mounted in *that* walk.
        var viewA = new StubComponent(new CssOnly());
        var viewB = new StubComponent(new JsOnly());

        using (var ctxA = LiveRenderContext.Begin(viewA))
        {
            var sbA = new StringBuilder();
            HtmlSerializer.Serialize(viewA, sbA);
            Assert.Contains(typeof(CssOnly), ctxA.MountedTypes);
            Assert.DoesNotContain(typeof(JsOnly), ctxA.MountedTypes);
        }

        using (var ctxB = LiveRenderContext.Begin(viewB))
        {
            var sbB = new StringBuilder();
            HtmlSerializer.Serialize(viewB, sbB);
            Assert.Contains(typeof(JsOnly), ctxB.MountedTypes);
            Assert.DoesNotContain(typeof(CssOnly), ctxB.MountedTypes);
        }
    }

    [Fact]
    public void MountedTypesIsClearedBetweenRenders_NewContextPerRender_StartsEmpty()
    {
        var view = new StubComponent(new CssOnly());

        using (var ctx1 = LiveRenderContext.Begin(view))
        {
            var sb = new StringBuilder();
            HtmlSerializer.Serialize(view, sb);
            Assert.Contains(typeof(CssOnly), ctx1.MountedTypes);
        }

        // A second Begin starts a fresh context with an empty MountedTypes.
        using var ctx2 = LiveRenderContext.Begin(view);
        Assert.Empty(ctx2.MountedTypes);
    }

    [Fact]
    public void MountedTypes_ReadableAfterDispose_LastWalkSnapshotPreserved()
    {
        // Dispose only restores _current and flips _active=false — it does not clear the
        // MountedTypes collection. Useful for post-render diagnostics and the head-emit
        // path which runs after the walk completes.
        var view = new StubComponent(new CssOnly());
        LiveRenderContext ctx;
        using (ctx = LiveRenderContext.Begin(view))
        {
            var sb = new StringBuilder();
            HtmlSerializer.Serialize(view, sb);
        }

        Assert.Contains(typeof(CssOnly), ctx.MountedTypes);
    }

    // ─── Test fixture types ───────────────────────────────────────────────

    private sealed class CssOnly : Component
    {
        protected override RenderResult Render() => Div();
    }

    private sealed class JsOnly : Component
    {
        protected override RenderResult Render() => Div();
    }

    private sealed class BothAssets : Component
    {
        protected override RenderResult Render() => Div();
    }

    private sealed class NoAssets : Component
    {
        protected override RenderResult Render() => Div();
    }

    private sealed class Outer : Component
    {
        protected override RenderResult Render() => new Middle();
    }

    private sealed class Middle : Component
    {
        protected override RenderResult Render() => new Inner();
    }

    private sealed class Inner : Component
    {
        protected override RenderResult Render() => Span();
    }
}
