using System.Collections.Concurrent;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// Two pages that each send the reader to the other. The server follows a redirect ten times and no further: the
// next page to navigate is told so by an exception, the reader is left on it, and the session goes on.
[Collection("DiagnosticsSink")]
public sealed class RedirectLoopTests
{
    [Fact]
    public async Task A_link_into_two_pages_that_redirect_to_each_other_ends_with_an_error_and_the_session_goes_on()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);
        using var reported = new CapturedDiagnostics();

        var looped = await redirects.Navigate("/redirect/ping");
        var after = await redirects.Navigate("/redirect/start");

        var stopped = Assert.Single(looped, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Matches("^/redirect/p[io]ng$", LiveFrames.HistoryUrl(stopped));
        Assert.Contains(reported.Errors, e => e.StartsWith("Too many redirects", StringComparison.Ordinal));
        Assert.Contains(after, f => f.Contains("start-content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_handler_that_goes_into_a_redirect_loop_ends_with_an_error_and_does_not_hang()
    {
        await using var redirects = await RedirectingSession.Open("/redirect/start", LiveDiffMode.DisabledFull);
        using var reported = new CapturedDiagnostics();

        var frames = await redirects.Click("to-ping");

        var stopped = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Matches("^/redirect/p[io]ng$", LiveFrames.HistoryUrl(stopped));
        Assert.Contains(reported.Errors, e => e.StartsWith("Too many redirects", StringComparison.Ordinal));
    }

    // The process-wide sink, swapped for the life of one test: the collection keeps two of these apart.
    private sealed class CapturedDiagnostics : IDisposable
    {
        private readonly ConcurrentQueue<RaskDiagnosticEvent> _events = new();
        private readonly Action<RaskDiagnosticEvent>? _previous = RaskDiagnostics.Sink;

        public CapturedDiagnostics() => RaskDiagnostics.Sink = _events.Enqueue;

        public IEnumerable<string> Errors => _events.Select(e => e.Exception?.Message ?? string.Empty);

        public void Dispose() => RaskDiagnostics.Sink = _previous;
    }
}
