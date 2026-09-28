namespace Rask.Core.Components;

/// <summary>Where the exception a boundary caught came from.</summary>
/// <remarks>
///     The distinction is load-bearing for the development error overlay: after a <see cref="Action" />
///     or <see cref="Lifecycle" /> fault the component tree is intact and the next render succeeds, so the
///     app can stay on screen with the error painted over it. After a <see cref="Render" /> fault it is
///     not — re-rendering the subtree that just threw would only throw again — so the fallback must
///     replace the page, in development as in production.
/// </remarks>
internal enum ErrorSource
{
    /// <summary>Thrown while rendering. The tree cannot be re-rendered as it stands.</summary>
    Render,

    /// <summary>Thrown by an event handler. The tree is intact.</summary>
    Action,

    /// <summary>Thrown by an async lifecycle hook, off the dispatch's call stack. The tree is intact.</summary>
    Lifecycle,
}
