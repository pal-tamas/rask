using Rask.Core.Live;

#pragma warning disable RASK014 // the pin needs the very instance it hands to the render context

namespace Rask.Core.Tests.Performance;

/// <summary>
///     What the framework-composed document costs per render, over and above the app's own body.
/// </summary>
/// <remarks>
///     <para>
///         The shell used to be the app's: its <c>Doctype</c>/<c>Html</c>/<c>Head</c>/<c>Body</c> were
///         four factory calls in the app's own <c>Render()</c>, and the framework's only per-render
///         addition was a scan of the finished page for four tokens. Now the same four elements are built
///         by <see cref="RootErrorBoundary" /> and the scan is gone. The bytes should have moved, not
///         grown — and this is the root, so whatever it costs is paid by every session on every frame.
///     </para>
///     <para>
///         Pinned as a DELTA against the identical body rendered as a bare root, so it measures the
///         composition and not the harness. Absolute numbers drift with unrelated work; this does not.
///     </para>
/// </remarks>
public class DocumentShellAllocationPinTests
{
    /// <summary>
    ///     The shell is five elements and one grouping fragment. It must not cost per render what a page
    ///     costs — no per-render dictionary, list or closure hiding in the composition.
    /// </summary>
    [Fact]
    public void Composing_the_document_costs_a_bounded_amount_per_render()
    {
        var composed = Measure(static () => new RootErrorBoundary(new ShellBodyProbe()));
        var bare = Measure(static () => new ShellBodyProbe());

        // Measured 624 B/render on 2026-08-08: the doctype and the four shell elements re-projected,
        // the grouping fragment, and the error boundary's own per-render Fragment — the same work the
        // app's own shell used to do in its own Render(). Pinned at twice that for jitter; a per-render
        // allocation that scales with the page (a fresh dictionary, a captured closure) clears it at
        // once, and the sibling test below pins that it does not scale at all.
        var delta = composed - bare;
        Assert.InRange(delta, 0, 1280);
    }

    /// <summary>
    ///     The composition is a fixed cost, not a per-node one: doubling the body must not move it.
    /// </summary>
    [Fact]
    public void The_documents_cost_does_not_scale_with_the_page()
    {
        var smallDelta = Measure(static () => new RootErrorBoundary(new ShellBodyProbe()))
                         - Measure(static () => new ShellBodyProbe());
        var largeDelta = Measure(static () => new RootErrorBoundary(new BigShellBodyProbe()))
                         - Measure(static () => new BigShellBodyProbe());

        Assert.True(
            Math.Abs(largeDelta - smallDelta) <= 512,
            $"shell cost moved with page size: {smallDelta} B vs {largeDelta} B per render");
    }

    // Steady-state cost of ONE more render of an already-mounted root: built once and re-rendered, so
    // mount-time allocation is warmed away and only the per-render work is measured. Mirrors
    // BuilderEntryAllocationPinTests.Measure, but through RenderAsLiveRoot — the shell only exists on
    // that path.
    private static long Measure(Func<Component> build)
    {
        var sp = RenderHarness.EmptyServices();
        var root = build();
        for (var i = 0; i < 200; i++)
        {
            root.RenderAsLiveRoot(sp);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int iterations = 1000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            root.RenderAsLiveRoot(sp);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    private sealed partial class ShellBodyProbe : Component
    {
        protected override Component? Render() =>
            Div(Id: "counter", Class: "counter")[
                Span(Class: "value")["42"],
                Button(Class: "inc")["+"]
            ];
    }

    private sealed partial class BigShellBodyProbe : Component
    {
        protected override Component? Render() =>
            Div(Id: "counter", Class: "counter")[
                Enumerable.Range(0, 40)
                    .Select(i => Div(Class: "row", Key: i)[Span()[i.ToString()], Button()["+"]])
            ];
    }
}
