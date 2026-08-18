using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     A running animation. An <c>Animation</c> object cannot cross interop, so the framework holds it
///     and hands back this handle — the same shape <see cref="MediaStreamId" /> uses for a
///     <c>MediaStream</c>.
/// </summary>
/// <param name="Value">The framework-minted id. <c>0</c> means the animation never started.</param>
public readonly record struct AnimationId(int Value)
{
    /// <summary>Whether this handle refers to an animation that actually started.</summary>
    public bool IsValid => Value > 0;
}

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

/// <summary>
///     Default <see cref="IWebAnimations" />, backed by the unified <see cref="IJSRuntime" /> and the
///     shared <c>__raskAnim</c> helper both client runtimes splice in.
/// </summary>
public sealed class WebAnimations(IJSRuntime js) : IWebAnimations
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskAnim.supported");

    /// <inheritdoc />
    public async ValueTask<AnimationId> StartAsync(
        ElementRef element,
        IReadOnlyDictionary<string, string[]> keyframes,
        AnimationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(keyframes);

        // A concrete Dictionary, not the interface: the source-generated JSON context registers
        // Dictionary<string, string[]>, and serializing through the interface would fall off that
        // contract and break the trimmed WASM publish.
        var frames = keyframes as Dictionary<string, string[]> ?? new Dictionary<string, string[]>(keyframes);
        var id = await js.InvokeAsync<int>("__raskAnim.start", element, frames, options ?? new AnimationOptions())
            .ConfigureAwait(false);
        return new AnimationId(id);
    }

    /// <inheritdoc />
    public ValueTask CancelAsync(AnimationId animation) => js.InvokeVoidAsync("__raskAnim.cancel", animation.Value);

    /// <inheritdoc />
    public ValueTask FinishAsync(AnimationId animation) => js.InvokeVoidAsync("__raskAnim.finish", animation.Value);

    /// <inheritdoc />
    public ValueTask PauseAsync(AnimationId animation) => js.InvokeVoidAsync("__raskAnim.pause", animation.Value);

    /// <inheritdoc />
    public ValueTask PlayAsync(AnimationId animation) => js.InvokeVoidAsync("__raskAnim.play", animation.Value);

    /// <inheritdoc />
    public ValueTask<bool> WaitAsync(AnimationId animation) =>
        js.InvokeAsync<bool>("__raskAnim.finished", animation.Value);
}
