namespace Rask.Core.Live;

/// <summary>
///     The outcome of a render driven to quiescence.
/// </summary>
/// <param name="Html">The markup as it stood when the last wave finished.</param>
/// <param name="TimedOut">
///     Whether a wave gave up before its work settled — the page is being served incomplete.
/// </param>
/// <param name="Waves">How many extra waves ran after the first render. Zero means it settled at once.</param>
public readonly record struct QuiescentRenderResult(string Html, bool TimedOut, int Waves);
