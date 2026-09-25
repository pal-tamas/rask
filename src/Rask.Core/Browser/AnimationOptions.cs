namespace Rask.Core.Browser;

/// <summary>
///     Timing for an animation, mapping onto the
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Element/animate">
///         <c>Element.animate()</c>
///     </see>
///     options object.
/// </summary>
/// <param name="DurationMs">How long one iteration runs.</param>
/// <param name="DelayMs">How long to wait before the first iteration.</param>
/// <param name="Easing">A CSS easing function — <c>ease-out</c>, <c>cubic-bezier(…)</c>, <c>linear</c>.</param>
/// <param name="Iterations">
///     How many times to run. <c>-1</c> means forever — JSON has no literal for <c>Infinity</c> and a
///     <see langword="double" /> infinity would not round-trip, so the wire spells it <c>-1</c>.
/// </param>
/// <param name="Direction"><c>normal</c>, <c>reverse</c>, <c>alternate</c>, <c>alternate-reverse</c>.</param>
/// <param name="Fill">
///     What the element looks like outside the animation's active period — <c>none</c> (the default),
///     <c>forwards</c>, <c>backwards</c>, <c>both</c>. Reach for <c>forwards</c> when the end state
///     should stick.
/// </param>
public sealed record AnimationOptions(
    double DurationMs = 400,
    double DelayMs = 0,
    string? Easing = null,
    int Iterations = 1,
    string? Direction = null,
    string? Fill = null);
