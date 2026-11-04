using Rask.Core.Diagnostics;

namespace Rask.UiTests.Live;

/// <summary>
///     What a test's act reported to the diagnostics sink. The sink is one for the process, so a class using this
///     runs in <see cref="ChoiceOffersCollection" />, which is not run beside another.
/// </summary>
internal static class Reports
{
    /// <summary>What <paramref name="act" /> reported to the diagnostics sink.</summary>
    public static async Task<List<RaskDiagnosticEvent>> Reported(Func<Task> act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        try
        {
            await act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        return captured;
    }

    /// <summary>The one warning <c>Rask.Forms</c> said of a refusal; it never repeats <paramref name="sent" />.</summary>
    public static RaskDiagnosticEvent Refusal(List<RaskDiagnosticEvent> reported, string sent)
    {
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Equal(RaskLogLevel.Warning, refused.Level);
        Assert.DoesNotContain(sent, refused.Message, StringComparison.Ordinal);

        return refused;
    }
}
