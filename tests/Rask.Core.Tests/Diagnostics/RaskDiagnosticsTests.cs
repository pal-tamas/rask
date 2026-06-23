using Rask.Core.Diagnostics;

namespace Rask.Core.Tests.Diagnostics;

// Joins the serialized ConsoleRedirect collection: these tests mutate the process-global
// RaskDiagnostics.Sink (including setting it to null), and the lifecycle tests in that collection
// capture Console.Error and assert that real faults reach the default (stderr) sink. Running in the
// same non-parallel collection keeps the two from racing on that shared global state.
[Collection("ConsoleRedirect")]
public class RaskDiagnosticsTests
{
    [Fact]
    public void Report_InvokesWiredSink_WithStructuredFields()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        try
        {
            var boom = new InvalidOperationException("boom");
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Test", "something faulted", boom);

            var e = Assert.Single(captured);
            Assert.Equal(RaskLogLevel.Error, e.Level);
            Assert.Equal("Rask.Test", e.Category);
            Assert.Equal("something faulted", e.Message);
            Assert.Same(boom, e.Exception);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    [Fact]
    public void Report_NullSink_IsNoOp()
    {
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = null;
        try
        {
            // Must not throw when diagnostics are silenced.
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Test", "ignored");
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    [Fact]
    public void ReportOnce_SameKey_ReportsOnlyOnce_AndBuildsMessageOnce()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var builds = 0;
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        RaskDiagnostics.ResetReportOnceForTests();
        try
        {
            var key = "test:" + Guid.NewGuid();
            string Factory()
            {
                builds++;
                return "first";
            }

            RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", Factory);
            RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", Factory);

            var e = Assert.Single(captured);
            Assert.Equal("first", e.Message);
            // The message factory runs only for the delivered event, never on the deduped repeat.
            Assert.Equal(1, builds);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
            RaskDiagnostics.ResetReportOnceForTests();
        }
    }

    [Fact]
    public void ReportOnce_DistinctKeys_EachReported()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        RaskDiagnostics.ResetReportOnceForTests();
        try
        {
            var prefix = "test:" + Guid.NewGuid() + ":";
            RaskDiagnostics.ReportOnce(prefix + "a", RaskLogLevel.Warning, "Rask.Test", () => "a");
            RaskDiagnostics.ReportOnce(prefix + "b", RaskLogLevel.Warning, "Rask.Test", () => "b");

            Assert.Equal(2, captured.Count);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
            RaskDiagnostics.ResetReportOnceForTests();
        }
    }

    [Fact]
    public void ReportOnce_NullSink_DoesNotBurnKey()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.ResetReportOnceForTests();
        try
        {
            var key = "test:" + Guid.NewGuid();

            // First occurrence fires while the sink is null — nothing delivered, key not burned.
            RaskDiagnostics.Sink = null;
            RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", () => "deferred");

            // Once a real sink is active, the same key still surfaces.
            RaskDiagnostics.Sink = captured.Add;
            RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", () => "deferred");

            var e = Assert.Single(captured);
            Assert.Equal("deferred", e.Message);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
            RaskDiagnostics.ResetReportOnceForTests();
        }
    }

    [Fact]
    public void FormatDefault_MessageOnly_RendersMessage()
    {
        var line = RaskDiagnostics.FormatDefault(
            new RaskDiagnosticEvent(RaskLogLevel.Warning, "Rask.Test", "just a message"));

        Assert.Equal("just a message", line);
    }

    [Fact]
    public void FormatDefault_WithException_AppendsExceptionAfterColon()
    {
        var ex = new InvalidOperationException("boom");
        var line = RaskDiagnostics.FormatDefault(
            new RaskDiagnosticEvent(RaskLogLevel.Error, "Rask.Test", "handler threw", ex));

        // Reproduces the framework's historical "<message>: <exception>" stderr format.
        Assert.StartsWith("handler threw: ", line);
        Assert.Contains("boom", line);
    }
}
