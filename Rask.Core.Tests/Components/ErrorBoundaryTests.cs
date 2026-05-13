using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class ErrorBoundaryTests
{
    [Fact]
    public void Render_NoError_RendersChildren()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(new Child[] { Span()[Text("ok")] }, fallback: null, resetKeys: null);

        Assert.Equal("<span>ok</span>", boundary.ToHtml());
    }

    [Fact]
    public void Render_DescendantRenderThrows_RendersFallback()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Child[] { new ThrowingRender("kaboom") },
            (ex, _) => Span()[Text(ex.Message)],
            resetKeys: null);

        Assert.Equal("<span>kaboom</span>", boundary.ToHtml());
    }

    [Fact]
    public void Render_DescendantRenderThrows_RewindsPartialOutput()
    {
        // The throwing child emits its opening <div> before the inner throw fires. The
        // boundary's rewind must remove that partial output so nothing leaks into the
        // serialized HTML around the fallback.
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Child[] { new ThrowMidwayComponent() },
            (ex, _) => Span()[Text("fb")],
            resetKeys: null);

        var html = boundary.ToHtml();
        Assert.Equal("<span>fb</span>", html);
        Assert.DoesNotContain("<div", html);
    }

    [Fact]
    public void Render_FallbackOmitted_UsesDefaultErrorPage()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Child[] { new ThrowingRender("dflt") },
            fallback: null,
            resetKeys: null);

        var html = boundary.ToHtml();
        Assert.Contains("rask-error-boundary", html);
        Assert.Contains("Something went wrong", html);
        Assert.Contains("dflt", html);
    }

    [Fact]
    public void Render_NestedBoundaries_InnerCatchesFirst()
    {
        var outerCaught = false;
        var inner = ErrorBoundary();
        inner.SetProps(
            new Child[] { new ThrowingRender("inner") },
            (ex, _) => Span()[Text("INNER:" + ex.Message)],
            resetKeys: null);

        var outer = ErrorBoundary();
        outer.SetProps(
            new Child[] { inner },
            (_, _) =>
            {
                outerCaught = true;
                return Span()[Text("OUTER")];
            },
            resetKeys: null);

        var html = outer.ToHtml();
        Assert.Equal("<span>INNER:inner</span>", html);
        Assert.False(outerCaught, "outer fallback should not run when inner catches");
    }

    [Fact]
    public void Render_NestedBoundaries_OuterCatchesWhenInnerFallbackThrows()
    {
        var inner = ErrorBoundary();
        inner.SetProps(
            new Child[] { new ThrowingRender("first") },
            (_, _) => throw new InvalidOperationException("fallback-broke"),
            resetKeys: null);

        var outer = ErrorBoundary();
        outer.SetProps(
            new Child[] { inner },
            (ex, _) => Span()[Text("OUTER:" + ex.Message)],
            resetKeys: null);

        Assert.Equal("<span>OUTER:fallback-broke</span>", outer.ToHtml());
    }

    [Fact]
    public void Recover_ClearsErrorAndNextRenderShowsChildren()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: true) },
            (ex, recover) => Button(OnClick: recover)[Text("retry:" + ex.Message)],
            resetKeys: null);

        // First render: boundary trips on the throw, emits fallback.
        var trippedHtml = boundary.ToHtml();
        Assert.Contains("retry:", trippedHtml);

        // Now simulate "fix the cause" then call Recover.
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: false) },
            (ex, recover) => Button(OnClick: recover)[Text("retry:" + ex.Message)],
            resetKeys: null);
        boundary.Recover();

        Assert.Equal("<span>ok</span>", boundary.ToHtml());
    }

    [Fact]
    public void ResetKeys_Change_AutoRecovers()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: true) },
            (ex, _) => Span()[Text("fb")],
            resetKeys: new object?[] { "v1" });

        var trippedHtml = boundary.ToHtml();
        Assert.Equal("<span>fb</span>", trippedHtml);

        // ResetKey changes from "v1" -> "v2" AND we fix the underlying cause.
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: false) },
            (ex, _) => Span()[Text("fb")],
            resetKeys: new object?[] { "v2" });

        // Auto-recover fires inside SetProps; next render walks Children again.
        Assert.Equal("<span>ok</span>", boundary.ToHtml());
    }

    [Fact]
    public void ResetKeys_SameValue_DoesNotAutoRecover()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: true) },
            (ex, _) => Span()[Text("fb:" + ex.Message)],
            resetKeys: new object?[] { "v1" });

        // First render trips.
        Assert.Contains("fb:", boundary.ToHtml());

        // Re-apply props with the SAME ResetKey value and a fixed child. Error must
        // persist because the boundary didn't observe a key change.
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: false) },
            (ex, _) => Span()[Text("fb:" + ex.Message)],
            resetKeys: new object?[] { "v1" });

        Assert.Contains("fb:", boundary.ToHtml());
    }

    [Fact]
    public void ResetKeys_AnyElementChange_AutoRecovers()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: true) },
            (ex, _) => Span()[Text("fb")],
            resetKeys: new object?[] { 1, "stable" });

        Assert.Equal("<span>fb</span>", boundary.ToHtml());

        // Only the second element changed; auto-recovery should still fire.
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: false) },
            (ex, _) => Span()[Text("fb")],
            resetKeys: new object?[] { 1, "changed" });

        Assert.Equal("<span>ok</span>", boundary.ToHtml());
    }

    [Fact]
    public void ResetKeys_LengthChange_AutoRecovers()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: true) },
            (ex, _) => Span()[Text("fb")],
            resetKeys: new object?[] { 1 });

        Assert.Equal("<span>fb</span>", boundary.ToHtml());

        boundary.SetProps(
            new Child[] { new ConditionalThrow(shouldThrow: false) },
            (ex, _) => Span()[Text("fb")],
            resetKeys: new object?[] { 1, 2 });

        Assert.Equal("<span>ok</span>", boundary.ToHtml());
    }

    [Fact]
    public void DescendantBoundary_StampedDuringSerialization()
    {
        // The default branch of HtmlSerializer stamps Component.Boundary on descendants
        // so async lifecycle / event-handler catch sites can find the right boundary.
        // This test asserts the stamp happens.
        var sp = new ServiceCollection().BuildServiceProvider();
        var probe = new BoundaryProbe();
        var boundary = ErrorBoundary();
        boundary.SetProps(new Child[] { probe }, fallback: null, resetKeys: null);

        using (LiveRenderContext.Begin(boundary, sp))
        {
            _ = boundary.ToHtml();
        }

        Assert.Same(boundary, probe.CapturedBoundary);
    }

    [Fact]
    public void DescendantStampedAtFirstTraversal_DoesNotOverwrite()
    {
        // Second-render must not clobber a boundary already assigned during the first
        // walk — that would lose the link when nested boundaries swap fallbacks.
        var sp = new ServiceCollection().BuildServiceProvider();
        var probe = new BoundaryProbe();
        var first = ErrorBoundary();
        first.SetProps(new Child[] { probe }, fallback: null, resetKeys: null);
        using (LiveRenderContext.Begin(first, sp))
        {
            _ = first.ToHtml();
        }

        Assert.Same(first, probe.CapturedBoundary);

        // Now reparent the probe under a different boundary. The stamp should NOT change.
        var second = ErrorBoundary();
        second.SetProps(new Child[] { probe }, fallback: null, resetKeys: null);
        using (LiveRenderContext.Begin(second, sp))
        {
            _ = second.ToHtml();
        }

        Assert.Same(first, probe.CapturedBoundary);
    }

    private sealed class ThrowingRender : Component
    {
        private readonly string _msg;
        public ThrowingRender(string msg) => _msg = msg;
        protected override Component Render() => throw new InvalidOperationException(_msg);
    }

    private sealed class ThrowMidwayComponent : Component
    {
        protected override Component Render() =>
            Div()[Text("partial"),
                new ThrowingRender("late")];
    }

    private sealed class ConditionalThrow : Component
    {
        private readonly bool _throw;
        public ConditionalThrow(bool shouldThrow) => _throw = shouldThrow;

        protected override Component Render() =>
            _throw ? throw new InvalidOperationException("bang") : Span()[Text("ok")];
    }

    private sealed class BoundaryProbe : Component
    {
        public ErrorBoundary? CapturedBoundary => Boundary;
        protected override Component Render() => Span()[Text("probe")];
    }
}
