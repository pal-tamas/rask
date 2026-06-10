namespace Rask.Core.Routing;

/// <summary>
///     The current location — path plus query string — for the live session, shared by the
///     <see cref="Router" />, <see cref="Navigator" />, and any component that injects it. Scoped
///     per session/circuit. Inject it (<c>public MyPage(RouteState route)</c>) to read the live
///     URL; mutate it through <see cref="Navigator" /> rather than by setting these properties
///     directly so browser history stays in sync.
///     <para>
///         Setting <see cref="Path" /> or <see cref="Query" /> raises <see cref="Changed" /> only
///         when the value actually differs, which is what drives the router to re-match. Components
///         that render off the URL outside the routed page subtree should subscribe to
///         <see cref="Changed" /> in <c>OnMount</c> and unsubscribe in <c>OnUnmount</c>.
///     </para>
/// </summary>
public sealed class RouteState
{
    private string _path = "/";
    private IQueryCollection _query = QueryCollection.Empty;

    /// <summary>
    ///     The current path (no query string), always starting with <c>/</c>. Defaults to <c>"/"</c>.
    ///     Setting a new value raises <see cref="Changed" />.
    /// </summary>
    public string Path
    {
        get => _path;
        set
        {
            if (_path == value)
            {
                return;
            }

            _path = value;
            Changed?.Invoke();
        }
    }

    /// <summary>
    ///     The current parsed query string. Defaults to an empty collection. Setting a new instance
    ///     raises <see cref="Changed" /> (compared by reference, so reuse the same instance for a
    ///     no-op).
    /// </summary>
    public IQueryCollection Query
    {
        get => _query;
        set
        {
            if (ReferenceEquals(_query, value))
            {
                return;
            }

            _query = value;
            Changed?.Invoke();
        }
    }

    /// <summary>
    ///     Raised whenever <see cref="Path" /> or <see cref="Query" /> changes. Subscribe in
    ///     <c>OnMount</c> and unsubscribe in <c>OnUnmount</c>. Components inside the routed page
    ///     subtree usually don't need this — the router re-renders them on navigation.
    /// </summary>
    public event Action? Changed;
}
