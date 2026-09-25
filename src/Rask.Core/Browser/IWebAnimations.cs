namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Animations_API">Web Animations
///     API</see> — run and control an animation on an element from C#, without a stylesheet and without
///     an animation library.
///     <para>
///         Keyframes use the API's <em>object</em> form: a property name to the values it moves through,
///         <c>["opacity"] = ["0", "1"]</c>. That is what <c>Element.animate()</c> takes natively.
///     </para>
/// </summary>
/// <remarks>
///     <para>
///         <b>Reduced motion is yours to decide here</b>, unlike <see cref="IViewTransitions" />. These
///         are your animations, and only you know what each one is for — refusing to run a loading
///         spinner and refusing to run a decorative parallax are not the same call. Read the preference
///         with <c>IMediaQuery</c> and skip what should be skipped.
///     </para>
///     <para>
///         Pair it with <c>ElementRef.New()</c> in a field, exactly as the focus and scroll helpers are
///         used.
///     </para>
/// </remarks>
public interface IWebAnimations
{
    /// <summary>Whether this browser implements <c>Element.animate()</c>.</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Starts an animation and returns a handle to it. The handle is invalid
    ///     (<see cref="AnimationId.IsValid" /> is <see langword="false" />) when the element is not
    ///     attached or the browser lacks the API — starting is then inert rather than an error.
    /// </summary>
    /// <param name="element">The element to animate.</param>
    /// <param name="keyframes">
    ///     Property name to the values it moves through — <c>["transform"] = ["scale(0.9)", "scale(1)"]</c>.
    /// </param>
    /// <param name="options">Timing. Defaults to 400 ms, no delay, one iteration.</param>
    ValueTask<AnimationId> StartAsync(
        ElementRef element,
        IReadOnlyDictionary<string, string[]> keyframes,
        AnimationOptions? options = null);

    /// <summary>Stops it and reverts the element. Harmless on a handle that has already finished.</summary>
    ValueTask CancelAsync(AnimationId animation);

    /// <summary>Jumps to the end. Harmless on a handle that has already finished.</summary>
    ValueTask FinishAsync(AnimationId animation);

    /// <summary>Pauses in place.</summary>
    ValueTask PauseAsync(AnimationId animation);

    /// <summary>Resumes a paused animation.</summary>
    ValueTask PlayAsync(AnimationId animation);

    /// <summary>
    ///     Waits for it to end. <see langword="true" /> when it ran to completion,
    ///     <see langword="false" /> when it was cancelled or the handle is already gone.
    ///     <para>
    ///         It does not throw on cancel, so awaiting it needs no <c>try</c>/<c>catch</c> — a cancelled
    ///         animation is an ordinary outcome, not an exception.
    ///     </para>
    /// </summary>
    ValueTask<bool> WaitAsync(AnimationId animation);
}
