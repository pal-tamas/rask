namespace Rask.Core.Live;

/// <summary>
///     Which routes can be prerendered, and which cannot.
/// </summary>
/// <param name="Paths">
///     Routes whose every segment is a literal, so the path is known without data. These are what a
///     prerender pass walks.
/// </param>
/// <param name="Skipped">
///     Routes that were left out, as their templates. <b>Report these.</b> A route carrying a
///     parameter cannot be enumerated without knowing the values, and a catch-all is a 404 page at
///     best — dropping them quietly would let a pass that covered only the static half read as
///     though it had covered everything.
/// </param>
public sealed record PrerenderPlan(IReadOnlyList<string> Paths, IReadOnlyList<string> Skipped);
