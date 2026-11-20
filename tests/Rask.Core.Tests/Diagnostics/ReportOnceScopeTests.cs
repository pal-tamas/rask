using Rask.Core.Diagnostics;

namespace Rask.Core.Tests.Diagnostics;

// A capture used to clear the PROCESS's ReportOnce record on install, so tests in parallel heard each other's
// once-only diagnostics again. A scope gives one flow a record of its own.
[Collection("ConsoleRedirect")]
public class ReportOnceScopeTests
{
    [Fact]
    public void A_scope_hears_a_key_the_process_already_heard_and_resets_nobody_else()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        var key = "test:" + Guid.NewGuid();
        try
        {
            RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", () => "outside");
            using (RaskDiagnostics.BeginReportOnceScope())
            {
                RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", () => "inside");
                RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", () => "inside");
                RaskDiagnostics.ResetReportOnceForTests();
            }

            RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", () => "outside");

            Assert.Equal(["outside", "inside"], captured.Select(static e => e.Message));
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    [Fact]
    public async Task A_scope_follows_the_flow_into_what_it_awaits()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = e => { lock (captured) { captured.Add(e); } };
        var key = "test:" + Guid.NewGuid();
        try
        {
            RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", () => "outside");
            using (RaskDiagnostics.BeginReportOnceScope())
            {
                await Task.Run(() => RaskDiagnostics.ReportOnce(key, RaskLogLevel.Warning, "Rask.Test", () => "awaited"), TestContext.Current.CancellationToken);
            }

            Assert.Contains(captured, static e => e.Message == "awaited");
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }
}
