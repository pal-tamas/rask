using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IPageVisibility" />, backed by the unified <see cref="IJSRuntime" />. Both are
///     property reads the client returns directly (the dispatcher returns the value when the resolved
///     identifier isn't a function).
/// </summary>
public sealed class PageVisibilityInfo(IJSRuntime js) : IPageVisibility
{
    /// <inheritdoc />
    public async ValueTask<PageVisibility> GetStateAsync()
    {
        var state = await js.InvokeAsync<string?>("document.visibilityState");
        return state switch
        {
            "hidden" => PageVisibility.Hidden,
            "prerender" => PageVisibility.Prerender,
            _ => PageVisibility.Visible
        };
    }

    /// <inheritdoc />
    public ValueTask<bool> IsHiddenAsync() => js.InvokeAsync<bool>("document.hidden");
}
