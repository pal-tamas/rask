using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
