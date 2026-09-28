namespace Rask.Core.Browser;

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
