using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class ErrorBoundaryTests
{
    [Fact]
    public void Render_NoError_RendersChildren()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(new Component[] { Span()[Text("ok")] }, null);

        Assert.Equal("<span>ok</span>", boundary.ToHtml());
    }

    [Fact]
    public void Render_DescendantRenderThrows_RendersFallback()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Component[] { new ThrowingRender("kaboom") },
            (ex, _) => Span()[Text(ex.Message)]);

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
            new Component[] { new ThrowMidwayComponent() },
            (ex, _) => Span()[Text("fb")]);

        var html = boundary.ToHtml();
        Assert.Equal("<span>fb</span>", html);
        Assert.DoesNotContain("<div", html);
    }

    [Fact]
    public void Render_FallbackOmitted_UsesDefaultErrorPage()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Component[] { new ThrowingRender("dflt") },
            null);

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
            new Component[] { new ThrowingRender("inner") },
            (ex, _) => Span()[Text("INNER:" + ex.Message)]);

        var outer = ErrorBoundary();
        outer.SetProps(
            new Component[] { inner },
            (_, _) =>
            {
                outerCaught = true;
                return Span()[Text("OUTER")];
            });

        var html = outer.ToHtml();
        Assert.Equal("<span>INNER:inner</span>", html);
        Assert.False(outerCaught, "outer fallback should not run when inner catches");
    }

    [Fact]
    public void Render_NestedBoundaries_OuterCatchesWhenInnerFallbackThrows()
    {
        var inner = ErrorBoundary();
        inner.SetProps(
            new Component[] { new ThrowingRender("first") },
            (_, _) => throw new InvalidOperationException("fallback-broke"));

        var outer = ErrorBoundary();
        outer.SetProps(
            new Component[] { inner },
            (ex, _) => Span()[Text("OUTER:" + ex.Message)]);

        Assert.Equal("<span>OUTER:fallback-broke</span>", outer.ToHtml());
    }

    [Fact]
    public void Recover_ClearsErrorAndNextRenderShowsChildren()
    {
        var boundary = ErrorBoundary();
        boundary.SetProps(
            new Component[] { new ConditionalThrow(true) },
            (ex, recover) => Button(OnClick: recover)[Text("retry:" + ex.Message)]);

        // First render: boundary trips on the throw, emits fallback.
        var trippedHtml = boundary.ToHtml();
        Assert.Contains("retry:", trippedHtml);

        // Now simulate "fix the cause" then call Recover.
        boundary.SetProps(
            new Component[] { new ConditionalThrow(false) },
            (ex, recover) => Button(OnClick: recover)[Text("retry:" + ex.Message)]);
        boundary.Recover();

        Assert.Equal("<span>ok</span>", boundary.ToHtml());
    }

    [Fact]
    public void DescendantBoundary_StampedDuringSerialization()
    {
        // The default branch of HtmlSerializer stamps Component.Boundary on descendants
        // so async lifecycle / event-handler catch sites can find the right boundary.
        // This test asserts the stamp happens.
        var sp = RenderHarness.EmptyServices();
        var probe = new BoundaryProbe();
        var boundary = ErrorBoundary();
        boundary.SetProps(new Component[] { probe }, null);

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
        var sp = RenderHarness.EmptyServices();
        var probe = new BoundaryProbe();
        var first = ErrorBoundary();
        first.SetProps(new Component[] { probe }, null);
        using (LiveRenderContext.Begin(first, sp))
        {
            _ = first.ToHtml();
        }

        Assert.Same(first, probe.CapturedBoundary);

        // Now reparent the probe under a different boundary. The stamp should NOT change.
        var second = ErrorBoundary();
        second.SetProps(new Component[] { probe }, null);
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
        protected override Component? Render() => throw new InvalidOperationException(_msg);
    }

    private sealed class ThrowMidwayComponent : Component
    {
        protected override Component? Render() =>
            Div()[Text("partial"),
                new ThrowingRender("late")];
    }

    private sealed class ConditionalThrow : Component
    {
        private readonly bool _throw;
        public ConditionalThrow(bool shouldThrow) => _throw = shouldThrow;

        protected override Component? Render() =>
            _throw ? throw new InvalidOperationException("bang") : Span()[Text("ok")];
    }

    private sealed class BoundaryProbe : Component
    {
        public ErrorBoundary? CapturedBoundary => Boundary;
        protected override Component? Render() => Span()[Text("probe")];
    }
}
