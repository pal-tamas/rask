using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to CSS media queries from C# (the matchMedia API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Window/matchMedia" />) — evaluate a
///     query like <c>(min-width: 768px)</c> or a user preference such as dark mode or reduced motion, to
///     branch component logic the way CSS branches styles. Works on <b>both transports</b>; inject it
///     through a component constructor and read from an event handler or lifecycle hook.
/// </summary>
/// <remarks>
///     This is a one-shot evaluation (the value at call time), not a live subscription — re-read it when
///     you need a fresh answer (e.g. in <c>OnRenderedAsync</c>). <c>window.matchMedia</c> is universally
///     supported, so no capability gate is needed.
/// </remarks>
public interface IMediaQuery
{
    /// <summary>
    ///     Whether <paramref name="query" /> currently matches (<c>window.matchMedia(query).matches</c>).
    ///     An invalid query never throws — it simply doesn't match.
    /// </summary>
    ValueTask<bool> MatchesAsync(string query);

    /// <summary>Whether the user prefers a dark color scheme (<c>(prefers-color-scheme: dark)</c>).</summary>
    ValueTask<bool> PrefersDarkAsync();

    /// <summary>Whether the user prefers reduced motion (<c>(prefers-reduced-motion: reduce)</c>).</summary>
    ValueTask<bool> PrefersReducedMotionAsync();
}

/// <summary>
///     Default <see cref="IMediaQuery" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>matchMedia</c> returns a live <c>MediaQueryList</c>, so the evaluation goes through the
///     framework's <c>__raskApi.matchMedia</c> helper, which returns just the boolean <c>.matches</c>.
/// </summary>
public sealed class MediaQuery(IJSRuntime js) : IMediaQuery
{
    /// <inheritdoc />
    public ValueTask<bool> MatchesAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return js.InvokeAsync<bool>("__raskApi.matchMedia", query);
    }

    /// <inheritdoc />
    public ValueTask<bool> PrefersDarkAsync() => MatchesAsync("(prefers-color-scheme: dark)");

    /// <inheritdoc />
    public ValueTask<bool> PrefersReducedMotionAsync() => MatchesAsync("(prefers-reduced-motion: reduce)");
}
