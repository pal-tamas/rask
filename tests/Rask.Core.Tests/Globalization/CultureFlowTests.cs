using System.Globalization;
using Rask.Core.Globalization;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Globalization;

// The culture reaches a render because the walk ASKS the session for it, never because it flowed there.
// These tests pin that down, including the case that rules out every propagation-based design.
public partial class CultureFlowTests : global::Rask.Core.RaskMarkup, IDisposable
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public CultureFlowTests() => RaskCulture.IsEnabled = true;

    public void Dispose()
    {
        RaskCulture.ResetForTests();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [RestoreCulture]
    public void Render_ReadsTheSessionsCulture_NotTheThreads()
    {
        // The thread says German; the session says Hungarian. The session wins, because the walk reads
        // the handle rather than the ambient value.
        CultureInfo.CurrentCulture = German;

        var probe = new CultureProbe();
        probe.RenderHandle = new FixedCultureHandle(Hungarian);

        using var ctx = LiveRenderContext.Begin(probe);
        probe.ToHtml();

        Assert.Equal(Hungarian, probe.Seen);
    }

    [Fact]
    [RestoreCulture]
    public void Render_PinsTheAmbientCulture_ForCodeThatCannotBeRoutedThroughRask()
    {
        // BsDataGrid's sort is the real case: comparing two strings reaches Comparer<T>.Default and so a
        // linguistic comparison under the CURRENT culture, with no seam to intercept. The pin is what
        // makes that follow the session.
        CultureInfo.CurrentCulture = German;

        var probe = new AmbientProbe();
        probe.RenderHandle = new FixedCultureHandle(Hungarian);

        using (var ctx = LiveRenderContext.Begin(probe))
        {
            probe.ToHtml();
        }

        Assert.Equal(Hungarian, probe.SeenAmbient);

        // And the walk gives the thread back what it borrowed.
        Assert.Equal(German, CultureInfo.CurrentCulture);
    }

    [Fact]
    [RestoreCulture]
    public void Culture_SurvivesSuppressedExecutionContextFlow()
    {
        // THE test for this design. LifecycleSyncContext deliberately calls ExecutionContext.SuppressFlow()
        // so a continuation cannot inherit InHandlerScope. Since .NET Core, CultureInfo.CurrentCulture
        // lives in an AsyncLocal riding that same ExecutionContext — so a culture that had to FLOW to the
        // continuation's render would be lost exactly there, and so would an AsyncLocal of our own.
        //
        // Reproduce that hostile environment literally, then render inside it.
        CultureInfo.CurrentCulture = German;

        var probe = new CultureProbe();
        probe.RenderHandle = new FixedCultureHandle(Hungarian);

        CultureInfo? seenInsideSuppressedFlow = null;
        CultureInfo? ambientInsideSuppressedFlow = null;
        var done = new ManualResetEventSlim();

        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() =>
            {
                try
                {
                    // Negative control, sampled BEFORE the render pins anything. If this came back as
                    // German the suppression would not be doing what the test claims, and the assertion
                    // below would be passing for the wrong reason — a culture that flowed rather than
                    // one that was fetched.
                    ambientInsideSuppressedFlow = CultureInfo.CurrentCulture;

                    using var ctx = LiveRenderContext.Begin(probe);
                    probe.ToHtml();
                    seenInsideSuppressedFlow = probe.Seen;
                }
                finally
                {
                    done.Set();
                }
            });
        }

        Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "the suppressed-flow render never completed");

        Assert.False(
            German.Equals(ambientInsideSuppressedFlow),
            "ExecutionContext flow was not actually suppressed, so this test proves nothing");

        // Fetched from the session, across a boundary nothing can flow over.
        Assert.Equal(Hungarian, seenInsideSuppressedFlow);
    }

    [Fact]
    [RestoreCulture]
    public void ReadingCulture_MarksTheComponentAsDependingOnAmbientState()
    {
        // Without this mark the clean-subtree render cache would keep serving a subtree rendered in the
        // previous language after a switch. It lives inside RaskCulture.Current precisely so a component
        // author cannot forget it.
        var reader = new CultureProbe();
        reader.RenderHandle = new FixedCultureHandle(Hungarian);
        using (var ctx = LiveRenderContext.Begin(reader))
        {
            reader.ToHtml();
        }

        Assert.True(reader.ReadsAmbientStateInternal);

        var ignorer = new PlainProbe();
        ignorer.RenderHandle = new FixedCultureHandle(Hungarian);
        using (var ctx = LiveRenderContext.Begin(ignorer))
        {
            ignorer.ToHtml();
        }

        Assert.False(ignorer.ReadsAmbientStateInternal);
    }

    [Fact]
    [RestoreCulture]
    public void OneWalkRendersInOneCulture_EvenIfTheSessionChangesMidFlight()
    {
        // The context snapshots the culture in its constructor. A handler switching language while a
        // walk is in progress must not produce a half-translated page.
        var handle = new MutableCultureHandle(Hungarian);
        var probe = new TwiceReadingProbe(() => handle.Culture = German);
        probe.RenderHandle = handle;

        using var ctx = LiveRenderContext.Begin(probe);
        probe.ToHtml();

        Assert.Equal(Hungarian, probe.First);
        Assert.Equal(Hungarian, probe.Second);
    }

    private sealed class FixedCultureHandle(CultureInfo culture) : IRenderHandle
    {
        public Task RequestRenderAsync() => Task.CompletedTask;

        CultureInfo IRenderHandle.Culture => culture;
        CultureInfo IRenderHandle.UICulture => culture;
    }

    private sealed class MutableCultureHandle(CultureInfo culture) : IRenderHandle
    {
        public CultureInfo Culture { get; set; } = culture;

        public Task RequestRenderAsync() => Task.CompletedTask;

        CultureInfo IRenderHandle.Culture => Culture;
        CultureInfo IRenderHandle.UICulture => Culture;
    }

    private sealed class CultureProbe : Component
    {
        public CultureInfo? Seen { get; private set; }

        protected override Component Render()
        {
            Seen = Culture;
            return Div["x"];
        }
    }

    private sealed class AmbientProbe : Component
    {
        public CultureInfo? SeenAmbient { get; private set; }

        protected override Component Render()
        {
            // Deliberately NOT through Rask — this is the code the pin exists for.
            SeenAmbient = CultureInfo.CurrentCulture;
            return Div["x"];
        }
    }

    private sealed class PlainProbe : Component
    {
        protected override Component Render() => Div["x"];
    }

    private sealed class TwiceReadingProbe(Action between) : Component
    {
        public CultureInfo? First { get; private set; }
        public CultureInfo? Second { get; private set; }

        protected override Component Render()
        {
            First = Culture;
            between();
            Second = Culture;
            return Div["x"];
        }
    }
}
