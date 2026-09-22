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
///         <see cref="Changed" /> in <c>Mount</c> and unsubscribe in <c>Unmount</c>.
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
    ///     <c>Mount</c> and unsubscribe in <c>Unmount</c>. Components inside the routed page
    ///     subtree usually don't need this — the router re-renders them on navigation.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    ///     The route table this session navigates within, or <c>null</c> for every page in every assembly.
    /// </summary>
    /// <remarks>
    ///     Set by a host that serves more than one application, to the application the session was opened for,
    ///     so a live navigation and the session's <see cref="Router" /> resolve against the same table its
    ///     first request did (#1094). Without it a host-app session could render a mounted application's pages
    ///     inside its own document. A provider rather than a list, because the table changes under hot reload:
    ///     it is asked again on every resolution, which the registry's per-tree cache keeps cheap.
    /// </remarks>
    internal Func<IReadOnlyList<Route>>? Table { get; set; }

    /// <summary>The table to resolve against now: the session's application, or the whole registry.</summary>
    internal IReadOnlyList<Route> CurrentTable => Table?.Invoke() ?? RouteRegistry.BuildTree();
}
