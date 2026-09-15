using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Diagnostics;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     The error log, and the probe's rules for what goes into it: repeats counted, a render fault's path built as it
///     unwinds, framework diagnostics filtered, and nothing listed twice.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed class DevToolsErrorLogTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

#pragma warning disable RASK014 // components made by hand, standing in for the ones a walk would report
    private static Component Child() => new DevToolsTestChild();

    private static Component Frame() => new DevToolsTestFrame();
#pragma warning restore RASK014

    [Fact]
    public void The_same_error_again_counts_up_and_a_different_one_is_a_new_entry()
    {
        var log = new DevToolsErrorLog();

        log.Record(DevToolsErrorKind.Handler, false, "InvalidOperationException", "boom", null, "Row", 7, true, false, At);
        log.Record(DevToolsErrorKind.Handler, false, "InvalidOperationException", "boom", null, "Row", 7, true, false,
            At.AddSeconds(3));
        log.Record(DevToolsErrorKind.Handler, false, "InvalidOperationException", "other", null, "Row", 7, true, false, At);

        var errors = log.Snapshot();
        Assert.Equal(2, errors.Length);
        Assert.Equal(2, errors[0].Count);
        Assert.Equal(At.AddSeconds(3), errors[0].At);
        Assert.Equal(["Row"], errors[0].Path);
        Assert.Equal(1, errors[1].Count);
    }

    [Fact]
    public void A_path_is_built_innermost_first_and_handed_out_outermost_first()
    {
        var log = new DevToolsErrorLog();

        var entry = log.Record(DevToolsErrorKind.Render, false, "E", "m", null, "Row", 1, false, false, At);
        log.Enclosing(entry, "Board");
        log.Enclosing(entry, "App");

        Assert.Equal(["App", "Board", "Row"], Assert.Single(log.Snapshot()).Path);
    }

    [Fact]
    public void A_full_log_drops_its_oldest_and_a_long_stack_is_cut()
    {
        var log = new DevToolsErrorLog();
        for (var i = 0; i < DevToolsErrorLog.Capacity + 2; i++)
        {
            log.Record(DevToolsErrorKind.Diagnostic, true, "Rask.Test", "warning " + i, new string('x', DevToolsErrorLog.DetailLimit * 2),
                null, null, false, true, At);
        }

        var errors = log.Snapshot();
        Assert.Equal(DevToolsErrorLog.Capacity, errors.Length);
        Assert.Equal("warning 2", errors[0].Message);
        Assert.True(errors[0].Detail!.Length < DevToolsErrorLog.DetailLimit + 10);

        log.Clear();
        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void A_render_fault_is_listed_once_with_every_component_it_unwound_through()
    {
        var feeds = new DevToolsFeeds();
        var probe = new DevToolsProbe(feeds);
        var inner = Child();
        var outer = Frame();
        var exception = new InvalidOperationException("the render failed");

        // Outside any page's work, so the fault is app-wide; the filter sees it once per enclosing component.
        Assert.False(probe.ObserveThrow(inner, exception));
        Assert.False(probe.ObserveThrow(outer, exception));

        var error = Assert.Single(feeds.AppWide.Snapshot());
        Assert.Equal(DevToolsErrorKind.Render, error.Kind);
        Assert.Equal("InvalidOperationException", error.Title);
        Assert.Equal("the render failed", error.Message);
        Assert.Equal([nameof(DevToolsTestFrame), nameof(DevToolsTestChild)], error.Path);
        Assert.NotNull(error.ComponentId);
        Assert.True(error.AppWide);

        // The framework's own report of the same exception is not a second entry.
        probe.DiagnosticReported(new RaskDiagnosticEvent(RaskLogLevel.Error, "Rask.Live", "a render threw", exception));
        Assert.Single(feeds.AppWide.Snapshot());
    }

    [Fact]
    public void A_handler_fault_names_whether_a_boundary_caught_it_and_the_exception_inside_its_wrappers()
    {
        var feeds = new DevToolsFeeds();
        var probe = new DevToolsProbe(feeds);
        var wrapped = new AggregateException(new ArgumentException("bad input"));

        probe.ComponentFaulted(Child(), wrapped, ErrorSource.Action, caught: true);

        var error = Assert.Single(feeds.AppWide.Snapshot());
        Assert.Equal(DevToolsErrorKind.Handler, error.Kind);
        Assert.Equal("ArgumentException", error.Title);
        Assert.Equal("bad input", error.Message);
        Assert.True(error.Caught);
    }

    [Fact]
    public void Framework_warnings_and_errors_are_listed_and_information_is_not()
    {
        var feeds = new DevToolsFeeds();
        var probe = new DevToolsProbe(feeds);

        probe.DiagnosticReported(new RaskDiagnosticEvent(RaskLogLevel.Information, "Rask.Live", "started"));
        probe.DiagnosticReported(new RaskDiagnosticEvent(RaskLogLevel.Warning, "Rask.Diff", "two siblings share a key"));
        probe.DiagnosticReported(new RaskDiagnosticEvent(
            RaskLogLevel.Error, "Rask.JsInvoke", "an invoke failed", new TimeoutException("no answer")));

        var errors = feeds.AppWide.Snapshot();
        Assert.Equal(["Rask.Diff", "Rask.JsInvoke"], errors.Select(e => e.Title));
        Assert.True(errors[0].IsWarning);
        Assert.False(errors[1].IsWarning);
        Assert.Equal("an invoke failed: no answer", errors[1].Message);
        Assert.Contains("TimeoutException", errors[1].Detail);
    }
}
