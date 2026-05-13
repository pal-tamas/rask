namespace Rask.Core.Components;

public sealed class ErrorBoundary : Component
{
    private bool _hasResetKeys;
    private object?[]? _resetKeysSnapshot;

    public Func<Exception, Action, Child>? Fallback { get; set; }
    public IReadOnlyList<object?>? ResetKeys { get; set; }

    internal Exception? Error { get; private set; }

    // Sugar for tests that construct an ErrorBoundary directly and need to seed all three
    // props in one call. Identical to assigning the properties individually and then
    // letting OnPropsChanged run.
    internal void SetProps(
        IEnumerable<Child>? children,
        Func<Exception, Action, Child>? fallback,
        IReadOnlyList<object?>? resetKeys)
    {
        Children = children;
        Fallback = fallback;
        ResetKeys = resetKeys;
        OnPropsChanged();
    }

    // Boundary state (Error) lives outside the framework's prop/state diff, so the cached
    // render result would never reflect a Trip(). BypassRenderCache forces Render() to run
    // every frame — same opt-out Router/DefaultNotFoundPage use for context-derived state.
    protected internal override bool BypassRenderCache => true;

    protected override void OnPropsChanged()
    {
        // Snapshot ResetKeys to a fresh array each render so a caller mutating the source
        // list between renders can't poison the equality check. First call seeds without
        // resetting; subsequent calls clear the error when any element has changed by
        // Equals (React's useEffect-deps semantics).
        var snapshot = ResetKeys?.ToArray();
        if (_hasResetKeys && KeysChanged(_resetKeysSnapshot, snapshot))
        {
            Error = null;
        }

        _resetKeysSnapshot = snapshot;
        _hasResetKeys = true;
    }

    private static bool KeysChanged(object?[]? previous, object?[]? current)
    {
        if (previous is null && current is null)
        {
            return false;
        }

        if (previous is null || current is null || previous.Length != current.Length)
        {
            return true;
        }

        for (var i = 0; i < previous.Length; i++)
        {
            if (!Equals(previous[i], current[i]))
            {
                return true;
            }
        }

        return false;
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

    protected override Component Render()
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
