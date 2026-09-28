using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IBadge" />, backed by the unified <see cref="IJSRuntime" />. The optional-argument
///     shape of <c>setAppBadge()</c> (no arg = dot, number = count) and the feature check go through the
///     framework's <c>__raskBadge</c> helper so the C# surface stays a clean nullable <c>int</c>.
/// </summary>
public sealed class Badge(IJSRuntime js) : IBadge
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskBadge.isSupported");

    /// <inheritdoc />
    public ValueTask SetAsync(int? count = null) => js.InvokeVoidAsync("__raskBadge.set", count);

    /// <inheritdoc />
    public ValueTask ClearAsync() => js.InvokeVoidAsync("__raskBadge.clear");
}
