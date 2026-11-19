using Rask.Core.Diagnostics;

namespace Rask.Core.Tests.Diagnostics;

// Past 1024 distinct keys ReportOnce used to drop every report in the process for good — the constant-key warnings
// (persisted state over budget, a model graph too large) among them. The oldest key is forgotten instead.
[Collection("ConsoleRedirect")]
public class ReportOnceEvictionTests
{
    [Fact]
    public void A_new_key_is_still_reported_after_a_thousand_and_more_distinct_ones()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        RaskDiagnostics.ResetReportOnceForTests();
        try
        {
            var prefix = "test:" + Guid.NewGuid() + ":";
            for (var i = 0; i < 2000; i++)
            {
                RaskDiagnostics.ReportOnce(prefix + i, RaskLogLevel.Warning, "Rask.Test", () => "churn");
            }

            RaskDiagnostics.ReportOnce(prefix + "late", RaskLogLevel.Warning, "Rask.Test", () => "late");
            RaskDiagnostics.ReportOnce(prefix + "late", RaskLogLevel.Warning, "Rask.Test", () => "late");

            Assert.Equal(2001, captured.Count);
            Assert.Single(captured, static e => e.Message == "late");
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
            RaskDiagnostics.ResetReportOnceForTests();
        }
    }
}
