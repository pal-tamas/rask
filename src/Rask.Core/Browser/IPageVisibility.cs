using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>Whether the page is currently visible to the user (the Page Visibility API).</summary>
public enum PageVisibility
{
    /// <summary>The page is at least partially visible (foreground tab, not minimized).</summary>
    Visible,

    /// <summary>The page is not visible (background tab, minimized window, or device locked).</summary>
    Hidden,

    /// <summary>The page is being pre-rendered and is not yet visible.</summary>
    Prerender
}

/// <summary>
///     Typed access to the Page Visibility API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Page_Visibility_API" />) — tell
///     whether the page is in the foreground, e.g. to pause polling or animation when the user tabs away.
///     Inject it through a component constructor and read from a lifecycle hook or event handler.
/// </summary>
/// <remarks>
///     These are one-shot reads of <c>document.visibilityState</c> / <c>document.hidden</c>. Reacting to
///     the <c>visibilitychange</c> event (push notifications to C#) is a later increment.
/// </remarks>
public interface IPageVisibility
{
    /// <summary>Reads the current visibility state (<c>document.visibilityState</c>).</summary>
    ValueTask<PageVisibility> GetStateAsync();

    /// <summary>Whether the page is currently hidden (<c>document.hidden</c>).</summary>
    ValueTask<bool> IsHiddenAsync();
}

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
