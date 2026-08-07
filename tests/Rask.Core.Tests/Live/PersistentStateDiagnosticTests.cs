using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

/// <summary>
///     Split out of <see cref="PersistentStateTests" /> because this is the one test in that file which
///     touches process-global state, and xUnit binds collections per class.
/// </summary>
/// <remarks>
///     Two globals are in play, and only one of them is fixed by joining a serialised collection:
///     <list type="bullet">
///         <item>
///             <c>RaskDiagnostics.Sink</c> — two tests swapping it concurrently lose it entirely (both
///             save <c>previous</c>, the second saves the first's capturing delegate, and restoring in
///             that order leaves the sink pointing at a list nobody reads). The collection prevents that.
///         </item>
///         <item>
///             The <c>ReportedOnce</c> dedup set behind <c>ReportOnce</c>, which is why the reset is
///             needed on the way in: another test may already have burned this call site's key, and the
///             overflow would then be suppressed and the capture empty.
///         </item>
///     </list>
///     What the collection does <em>not</em> buy is isolation from the rest of the assembly — xUnit runs
///     other collections in parallel, and every diagnostic any of them emits lands in the sink installed
///     here. Asserting <c>Assert.Single</c> over the whole capture therefore asserted "no other test in
///     Rask.Core.Tests reported anything in this millisecond", which is true almost always and so failed
///     rarely and confusingly (#617). The assertion is scoped to the events this call site produces
///     instead — and stays strict about those, so a second report from the budget check still fails.
/// </remarks>
[Collection("ConsoleRedirect")]
public class PersistentStateDiagnosticTests
{
    /// <summary>The overflow is reported to the developer, not swallowed — it explains a reload they'd otherwise chase.</summary>
    [Fact]
    public void Exceeding_the_budget_reports_a_diagnostic()
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.ResetReportOnceForTests();
        RaskDiagnostics.Sink = captured.Add;
        try
        {
            var state = new PersistentState { MaxBytes = 32 };
            state.Persist("big", new string('x', 256));
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
            RaskDiagnostics.ResetReportOnceForTests();
        }

        var overflow = captured
            .Where(e => e.Category == "Rask.Live"
                        && e.Message.Contains("budget", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var warning = Assert.Single(overflow);
        Assert.Equal(RaskLogLevel.Warning, warning.Level);
        Assert.Contains("resumable", warning.Message, StringComparison.OrdinalIgnoreCase);
    }
}
