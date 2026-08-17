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

public sealed class ErrorBoundary : Component
{
    public Func<Exception, Action, Component>? Fallback { get; set; }

    internal Exception? Error { get; private set; }

    /// <summary>Where <see cref="Error" /> came from. Meaningless when <see cref="Error" /> is null.</summary>
    /// <remarks>
    ///     This used to need <c>new</c>: the builder surface gave <see cref="Component" /> an entry named
    ///     after every tag, and one of them is <c>&lt;source&gt;</c>. The tag family lives in
    ///     <c>Rask.Html</c> now, so Core inherits no <c>Source</c> entry and there is nothing left to
    ///     hide — the collision, and the CS0108 it caused, are gone with it.
    /// </remarks>
    internal ErrorSource Source { get; private set; }

    // Boundary state (Error) lives outside the framework's prop/state diff, so the cached
    // render result would never reflect a Trip(). BypassRenderCache forces Render() to run
    // every frame — same opt-out Router/DefaultNotFoundPage use for context-derived state.
    protected override bool BypassRenderCache => true;

    // Sugar for tests that construct an ErrorBoundary directly and need to seed both
    // props in one call.
    internal void SetProps(
        IEnumerable<Component>? children,
        Func<Exception, Action, Component>? fallback)
    {
        Children = children;
        Fallback = fallback;
    }

    internal void Trip(Exception ex, ErrorSource source = ErrorSource.Render)
    {
        Error = ex;
        Source = source;
        StateHasChanged();
    }

    /// <summary>
    ///     Records the error <b>without</b> asking for a render — the mirror of
    ///     <see cref="ClearErrorInRender" />, for a caller that is already inside the render which will
    ///     display the fallback (<c>RootErrorBoundary</c>, when the App's <c>Shell</c> override throws).
    ///     <see cref="Trip" /> would signal a render from inside the render that is about to show the
    ///     error, and since the same override throws again the frame after, that is a loop.
    /// </summary>
    internal void TripInRender(Exception ex)
    {
        Error = ex;
        Source = ErrorSource.Render;
    }

    /// <summary>
    ///     Clears the error <b>without</b> asking for a render — for a caller already inside the render
    ///     that is about to display the app anyway (the development overlay). <see cref="Recover" /> is the
    ///     one to use from a handler.
    /// </summary>
    internal void ClearErrorInRender() => Error = null;

    internal void SetParentBoundary(ErrorBoundary? parent)
    {
        // Stamp on first push so an error in this boundary's *own* fallback subtree (or
        // an async fault on this boundary itself) bubbles to the outer boundary.
        if (Boundary is null)
        {
            Boundary = parent;
        }
    }

    public void Recover()
    {
        if (Error is null)
        {
            return;
        }

        Error = null;
        StateHasChanged();
    }

    protected override Component? Render()
    {
        if (Error is null)
        {
            return new Fragment(Children);
        }

        if (Fallback is { } fallback)
        {
            // Pass Recover as a captured Action so the fallback subtree can register it as
            // an event handler (e.g. `Button(OnClick: recover)`) without the user needing
            // to capture the boundary instance themselves.
            return new Fragment(fallback(Error, Recover));
        }

        return new DefaultErrorPage(Error);
    }
}
