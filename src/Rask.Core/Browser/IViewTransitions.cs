using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/View_Transition_API">View Transition
///     API</see> — the browser animates between the old and new DOM instead of the new one simply
///     appearing.
///     <para>
///         This is the one Web API on this surface an app genuinely could not add for itself. A
///         same-document transition has to <em>wrap</em> the DOM mutation, and in Rask the mutation is the
///         framework's morph: there is no point in an app's code that sits around it. So enabling this
///         routes the live runtime's own commit — the diff apply and the full-document apply, on both the
///         Server and WASM hosts — through <c>document.startViewTransition</c>.
///     </para>
///     <para>
///         Off by default, and off is exactly today's behaviour: the commit runs synchronously, as it
///         always has. That is deliberate — deferring every app's DOM commit into a transition callback
///         is a timing change, and no app should get one it did not ask for.
///     </para>
/// </summary>
/// <remarks>
///     <para>
///         <b>Reduced motion is honoured for you.</b> What this drives is the browser's own default
///         cross-fade, so there is no stylesheet of yours for
///         <c>prefers-reduced-motion</c> to switch off. A reader who asked for less motion gets the plain
///         commit, and you need write nothing.
///     </para>
///     <para>
///         Style the transition with the standard <c>::view-transition-*</c> pseudo-elements and
///         <c>view-transition-name</c>, exactly as you would outside Rask. Give an element a stable
///         <c>view-transition-name</c> and the browser morphs it between routes rather than cross-fading
///         it — which is what makes a shared header or hero image travel.
///     </para>
/// </remarks>
public interface IViewTransitions
{
    /// <summary>
    ///     Whether this browser implements the API at all. <see langword="false" /> anywhere
    ///     <c>document.startViewTransition</c> is missing — enabling is then simply inert, never an error.
    /// </summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Turns transitions on or off for this session's subsequent renders. Returns the value actually
    ///     in effect.
    /// </summary>
    /// <param name="enabled">
    ///     <see langword="true" /> to wrap subsequent DOM commits in a view transition.
    /// </param>
    ValueTask<bool> SetEnabledAsync(bool enabled);

    /// <summary>
    ///     Whether a commit right now would actually animate — enabled, supported, and not overridden by
    ///     the reader's reduced-motion preference. Useful for a settings UI that wants to say why the
    ///     toggle it offers is having no effect.
    /// </summary>
    ValueTask<bool> IsActiveAsync();
}

/// <summary>
///     Default <see cref="IViewTransitions" />, backed by the unified <see cref="IJSRuntime" /> and the
///     shared <c>__raskVt</c> helper both client runtimes splice in.
/// </summary>
public sealed class ViewTransitions(IJSRuntime js) : IViewTransitions
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskVt.supported");

    /// <inheritdoc />
    public ValueTask<bool> SetEnabledAsync(bool enabled) => js.InvokeAsync<bool>("__raskVt.set", enabled);

    /// <inheritdoc />
    public ValueTask<bool> IsActiveAsync() => js.InvokeAsync<bool>("__raskVt.active");
}
