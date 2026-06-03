namespace Rask.Core.Components;

public sealed class ErrorBoundary : Component
{
    public Func<Exception, Action, Child>? Fallback { get; set; }

    internal Exception? Error { get; private set; }

    // Boundary state (Error) lives outside the framework's prop/state diff, so the cached
    // render result would never reflect a Trip(). BypassRenderCache forces Render() to run
    // every frame — same opt-out Router/DefaultNotFoundPage use for context-derived state.
    protected override bool BypassRenderCache => true;

    // Sugar for tests that construct an ErrorBoundary directly and need to seed both
    // props in one call.
    internal void SetProps(
        IEnumerable<Child>? children,
        Func<Exception, Action, Child>? fallback)
    {
        Children = children;
        Fallback = fallback;
    }

    internal void Trip(Exception ex)
    {
        Error = ex;
        StateHasChanged();
    }

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

    protected override RenderResult Render()
    {
        if (Error is null)
        {
            return new Fragment(Children);
        }

        if (Fallback is not null)
        {
            // Pass Recover as a captured Action so the fallback subtree can register it as
            // an event handler (e.g. `Button(OnClick: recover)`) without the user needing
            // to capture the boundary instance themselves.
            return new Fragment(Fallback(Error, Recover));
        }

        return new DefaultErrorPage(Error);
    }
}
