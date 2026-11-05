using Microsoft.Extensions.Primitives;
using Rask.Core.Live;

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
    private readonly RouteTitle _title = new();
    private string _path = "/";
    private IQueryCollection _query = QueryCollection.Empty;
    private Type? _forbidden;

    /// <summary>
    ///     The current path (no query string), always starting with <c>/</c>. Defaults to <c>"/"</c>.
    ///     Setting a new value raises <see cref="Changed" />.
    /// </summary>
    public string Path
    {
        get => _path;
        set
        {
            if (string.Equals(_path, value, StringComparison.Ordinal))
            {
                return;
            }

            _path = value;
            Changed?.Invoke(this, EventArgs.Empty);
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
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    ///     What the current page is called — its <see cref="Component.PageTitle" /> — or <c>null</c> when it
    ///     declares none: <c>route.Title is { } t ? Ui.BreadcrumbsItem[t] : null</c>.
    /// </summary>
    /// <remarks>
    ///     A component that reads it while rendering renders again when it changes — in the same render, so
    ///     the first HTML and a navigation's frame already carry the new one — and not otherwise. A page with no title of its
    ///     own takes the nearest layout's above it. A change does not raise <see cref="Changed" />.
    /// </remarks>
    public string? Title => _title.Read();

    /// <summary>
    ///     Raised whenever <see cref="Path" /> or <see cref="Query" /> changes. Subscribe in
    ///     <c>OnMount</c> and unsubscribe in <c>OnUnmount</c>. Components inside the routed page
    ///     subtree usually don't need this — the router re-renders them on navigation.
    /// </summary>
    public event EventHandler? Changed;

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

    /// <summary>
    ///     The page of the current route its signed-in person may not open, or <c>null</c> when they may open all of it.
    /// </summary>
    /// <remarks>
    ///     Set by the host from the route's guard, before the route renders. The router renders the layouts above that
    ///     page as usual and the forbidden sentence in its place, so the app's frame stays around the message. Setting a
    ///     new value raises <see cref="Changed" />.
    /// </remarks>
    internal Type? Forbidden
    {
        get => _forbidden;
        set
        {
            if (_forbidden == value)
            {
                return;
            }

            _forbidden = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    ///     The page being shown, in <paramref name="language" /> — the link a language switcher writes:
    ///     <c>Ui.Link.Href(route.In("de"))["Deutsch"]</c>, in an app that puts languages in its URLs
    ///     (<c>UseCulture(c =&gt; c.UrlPrefix = true)</c>).
    /// </summary>
    /// <remarks>
    ///     The same path and query, with the language prefix swapped: on <c>/en/about?tab=2</c>,
    ///     <c>route.In("de")</c> is <c>/de/about?tab=2</c> and <c>route.In("hu")</c>, for a default of
    ///     <c>hu</c>, is <c>/about?tab=2</c>. A component that writes one renders again when the page changes.
    ///     Another page in another language is <c>Routes.Contact().In("de")</c>.
    /// </remarks>
    /// <param name="language">A language the app ships, as a BCP&#160;47 tag: <c>"de"</c>.</param>
    public RouteUrl In(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        LiveRenderContext.CurrentSync?.MarkCurrentReadsAmbientState();

        return Here(Query) with { Language = language };
    }

    /// <summary>
    ///     The page being shown with <paramref name="changes" /> written into its query — a pair with no value takes its
    ///     key out, every other key stays: where a pager's link or a sort leads.
    /// </summary>
    /// <param name="changes">The keys to write, matched without regard to case.</param>
    /// <returns>An in-app link to this page, in the language of the page it is used on.</returns>
    internal RouteUrl With(IEnumerable<KeyValuePair<string, string?>> changes)
    {
        var query = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Query)
        {
            query[key] = value;
        }

        foreach (var (key, value) in changes)
        {
            if (value is null)
            {
                query.Remove(key);
            }
            else
            {
                query[key] = value;
            }
        }

        return Here(new QueryCollection(query));
    }

    // This page under the query given: its page type makes it an in-app link.
    private RouteUrl Here(IEnumerable<KeyValuePair<string, StringValues>> query)
    {
        var path = RoutePath;
        var page = RouteResolver.TryResolve(CurrentTable, path, out var chain, out var notFound) && !notFound && chain.Count > 0
            ? chain[^1]
            : null;
        var here = QueryString.Build(path, query);
        var at = here.IndexOf('?', StringComparison.Ordinal);
        return new RouteUrl(path, at < 0 ? null : here[at..], page);
    }

    /// <summary>
    ///     The languages this session writes into its URLs, or <c>null</c> when it keeps them out. Set by the host
    ///     from <see cref="Globalization.RaskCultureOptions.UrlPrefix" />, before the first render.
    /// </summary>
    internal UrlLanguages? Languages { get; set; }

    /// <summary>The language <see cref="Path" /> is in, or <c>null</c> when the session keeps languages out of its URLs.</summary>
    internal string? Language => Split()?.Language;

    /// <summary>
    ///     <see cref="Path" /> without its language prefix: what the routes are matched against.
    /// </summary>
    internal string RoutePath => Split()?.RoutePath ?? _path;

    // The path's language and route, worked out once per path: the router asks every frame and every link on the
    // page asks again. One reference, so a reader on another thread sees a whole answer or the previous one.
    private PathSplit? _split;

    private PathSplit? Split()
    {
        if (Languages is not { } languages)
        {
            return null;
        }

        var path = _path;
        if (_split is { } known && ReferenceEquals(known.Path, path) && ReferenceEquals(known.Languages, languages))
        {
            return known;
        }

        var (language, routePath) = languages.Split(path);
        return _split = new PathSplit(path, languages, language, routePath);
    }

    private sealed record PathSplit(string Path, UrlLanguages Languages, string Language, string RoutePath);

    /// <summary>The title as the pages of each walk declare it; the router and the render root drive it.</summary>
    internal RouteTitle TitleSource => _title;

    /// <summary>The table to resolve against now: the session's application, or the whole registry.</summary>
    internal IReadOnlyList<Route> CurrentTable => Table?.Invoke() ?? RouteRegistry.BuildTree();
}
