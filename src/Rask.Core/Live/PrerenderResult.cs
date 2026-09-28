namespace Rask.Core.Live;

/// <summary>
///     The outcome of prerendering one page.
/// </summary>
/// <param name="Html">The complete document.</param>
/// <param name="TimedOut">
///     Whether the page was still waiting on work when the budget ran out. Its markup is whatever had
///     rendered by then — a placeholder, in the case that matters.
/// </param>
/// <param name="Faulted">
///     Whether the render threw and the root boundary rendered its fallback instead of the app.
/// </param>
/// <param name="Waves">How many extra waves ran after the first render.</param>
/// <param name="Error">
///     What threw, when <paramref name="Faulted" /> is true; <c>null</c> otherwise. The flag is enough
///     to REFUSE the render — which is what every caller does — but not to fix it, and a build-time
///     pass has no browser console and no request log to fall back on. Report this, or a page the pass
///     declined to write is a URL that ships as an empty boot shell for a reason nobody can name.
/// </param>
public readonly record struct PrerenderResult(
    string Html,
    bool TimedOut,
    bool Faulted,
    int Waves,
    Exception? Error = null);
