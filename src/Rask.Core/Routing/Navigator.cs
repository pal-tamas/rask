using Microsoft.Extensions.Primitives;
using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>
///     The engine under <see cref="Rask.Core.Go" /> and <see cref="Rask.Core.Download" />: navigation, query-string
///     changes and file downloads for one session.
///     <para>
///         A handler's navigation is taken by its dispatch after the handler returns. One a page makes while it is
///         being rendered is a redirect: its frame is withheld and the dispatch renders the destination. One a
///         hook makes with no dispatch behind it — after an await, or as its page mounts on a reconnect — is
///         handed to the session, which makes it in turn with its event handlers. The first request is the
///         exception throughout: its navigation becomes the response's <c>302</c>.
///     </para>
///     <para>
///         Changes are applied to the shared <see cref="RouteState" /> and the resulting URL is pushed (or
///         replaced) into browser history by the live runtime.
///     </para>
/// </summary>
internal sealed class Navigator(RouteState routeState, IDownloadSink? downloadSink = null)
{
    // The navigator of the handler currently running on this async flow. Published by EnterHandler and
    // cleared when that scope disposes, so it is set for exactly the window in which navigation is legal —
    // the same window _inHandler describes. It exists so the generated `SomePage.Go(...)` static extensions
    // can navigate without a receiver to inject through: they have no instance and no DI to reach.
    //
    // AsyncLocal (not ThreadStatic) because a handler may await: the value has to flow into continuations
    // that resume on another pool thread. Per-session correctness comes from the DI registration — Server
    // registers Navigator per session scope, WASM as a singleton because that host owns one session —
    // so whichever navigator a dispatch entered is that dispatch's own.
    private static readonly AsyncLocal<Navigator?> _current = new();

    /// <summary>How many pages in a row may each send the reader on as they mount before it is a loop.</summary>
    internal const int MaxRedirects = 10;

    private bool _dirty;
    private bool _inHandler;
    private bool _inInitialRender;
    private bool _handedOverInWalk;
    private bool _redirected;
    private int _redirects;
    private bool _replace;

    /// <summary>
    ///     The <see cref="Navigator" /> for the event handler currently running, or <c>null</c> outside one.
    ///     The facades and the generated <c>SomePage.Go(...)</c> helpers reach it through this.
    /// </summary>
    public static Navigator? Current => _current.Value;

    /// <summary>
    ///     <see cref="Current" />, or a throw with the same actionable message the instance methods raise when
    ///     used outside an event handler.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static Navigator RequireCurrent() =>
        _current.Value
        ?? LiveRenderContext.Current?.Services?.GetService(typeof(Navigator)) as Navigator
        ?? throw new InvalidOperationException(
            "There is no session to navigate here. Go() works in an event handler (Button.OnClick(…)) and in a " +
            "page's lifecycle hooks (OnMount, OnUpdated), where it sends the reader on to another page; this " +
            "code is running outside both — a background task, or a component rendered with no host. Navigate " +
            "from a handler or a hook instead, and use [Authorize] to turn away a reader who is not signed in. " +
            "See docs/routing.md.");

    /// <summary>
    ///     Navigates to <paramref name="url" /> (path + query together). Pass a type-safe
    ///     <see cref="RouteUrl" /> from a generated <c>Routes.Page(...)</c> formatter.
    /// </summary>
    /// <param name="url">Target path and query string.</param>
    /// <param name="replace">
    ///     <c>true</c> replaces the current history entry instead of pushing a new one (no extra
    ///     Back-button stop).
    /// </param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void NavigateTo(RouteUrl url, bool replace = false)
    {
        if (HandedToTheSession(() => NavigateTo(url, replace)))
        {
            return;
        }

        BeforeNavigating();
        routeState.Path = url.Path;
        routeState.Query = string.IsNullOrEmpty(url.QueryString)
            ? QueryCollection.Empty
            : QueryString.Parse(url.QueryString);
        _replace = replace;
        Navigated();
    }

    /// <summary>
    ///     Navigates to <paramref name="path" />, <b>clearing any existing query string</b>. Use
    ///     <see cref="SetQuery(string, string?)" /> afterwards, or the query overload, to keep params.
    /// </summary>
    /// <param name="path">Target path (e.g. <c>"/users/42"</c>).</param>
    /// <param name="replace"><c>true</c> replaces the current history entry instead of pushing.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void NavigateTo(string path, bool replace = false)
    {
        if (HandedToTheSession(() => NavigateTo(path, replace)))
        {
            return;
        }

        BeforeNavigating();
        ArgumentNullException.ThrowIfNull(path);
        routeState.Path = path;
        routeState.Query = QueryCollection.Empty;
        _replace = replace;
        Navigated();
    }

    /// <summary>
    ///     Navigates to <paramref name="path" /> and <b>replaces</b> the query string with
    ///     <paramref name="query" /> in one step. Entries with a <c>null</c> value are dropped;
    ///     repeated keys are concatenated into a multi-value param.
    /// </summary>
    /// <param name="path">Target path.</param>
    /// <param name="query">The complete new query string as key/value pairs.</param>
    /// <param name="replace"><c>true</c> replaces the current history entry instead of pushing.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void NavigateTo(string path, IEnumerable<KeyValuePair<string, string?>> query, bool replace = false)
    {
        if (HandedToTheSession(() => NavigateTo(path, query, replace)))
        {
            return;
        }

        BeforeNavigating();
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        routeState.Path = path;
        routeState.Query = BuildCollection(query);
        _replace = replace;
        Navigated();
    }

    /// <summary>
    ///     Sets or updates a single query parameter on the <b>current</b> path (the path is left
    ///     unchanged). A <c>null</c> <paramref name="value" /> removes the key — equivalent to
    ///     <see cref="RemoveQuery" />.
    /// </summary>
    /// <param name="key">Query parameter name (case-insensitive).</param>
    /// <param name="value">New value, or <c>null</c> to remove the parameter.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void SetQuery(string key, string? value)
    {
        if (HandedToTheSession(() => SetQuery(key, value)))
        {
            return;
        }

        BeforeNavigating();
        ArgumentNullException.ThrowIfNull(key);
        var dict = ToDictionary(routeState.Query);
        if (value is null)
        {
            dict.Remove(key);
        }
        else
        {
            dict[key] = value;
        }

        routeState.Query = new QueryCollection(dict);
        Navigated();
    }

    /// <summary>
    ///     Sets or updates several query parameters on the current path in one update. Pairs with a
    ///     <c>null</c> value remove that key; all other params are preserved.
    /// </summary>
    /// <param name="values">Parameters to set or remove.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void SetQuery(params KeyValuePair<string, string?>[] values)
    {
        if (HandedToTheSession(() => SetQuery(values)))
        {
            return;
        }

        BeforeNavigating();
        ArgumentNullException.ThrowIfNull(values);
        var dict = ToDictionary(routeState.Query);
        foreach (var kv in values)
        {
            if (kv.Value is null)
            {
                dict.Remove(kv.Key);
            }
            else
            {
                dict[kv.Key] = kv.Value;
            }
        }

        routeState.Query = new QueryCollection(dict);
        Navigated();
    }

    /// <summary>Removes a single query parameter from the current path. Missing keys are a no-op.</summary>
    /// <param name="key">Query parameter name (case-insensitive).</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void RemoveQuery(string key)
    {
        if (HandedToTheSession(() => RemoveQuery(key)))
        {
            return;
        }

        BeforeNavigating();
        ArgumentNullException.ThrowIfNull(key);
        var dict = ToDictionary(routeState.Query);
        dict.Remove(key);
        routeState.Query = new QueryCollection(dict);
        Navigated();
    }

    /// <summary>Removes all query parameters from the current path, keeping the path.</summary>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void ClearQuery()
    {
        if (HandedToTheSession(() => ClearQuery()))
        {
            return;
        }

        BeforeNavigating();
        routeState.Query = QueryCollection.Empty;
        Navigated();
    }

    /// <summary>
    ///     Pushes a file to the browser as a download, from an in-memory byte array. Delivered over
    ///     the live channel by the host's <see cref="IDownloadSink" /> (registered by
    ///     <c>AddRask()</c> on the server and by the WASM host builder).
    /// </summary>
    /// <param name="filename">Suggested file name shown in the browser's save dialog.</param>
    /// <param name="bytes">File contents.</param>
    /// <param name="contentType">MIME type; defaults to <c>application/octet-stream</c> when null.</param>
    /// <exception cref="InvalidOperationException">
    ///     Called outside an event handler, or no <see cref="IDownloadSink" /> is registered.
    /// </exception>
    public void Download(string filename, byte[] bytes, string? contentType = null)
    {
        EnsureInHandler();
        ArgumentException.ThrowIfNullOrEmpty(filename);
        ArgumentNullException.ThrowIfNull(bytes);
        ResolveSink().Stage(filename, bytes, contentType);
    }

    /// <summary>
    ///     Pushes a file to the browser as a download, streaming from <paramref name="stream" />
    ///     (the sink reads and disposes it). Prefer this overload for large payloads.
    /// </summary>
    /// <param name="filename">Suggested file name shown in the browser's save dialog.</param>
    /// <param name="stream">Readable stream of file contents.</param>
    /// <param name="contentType">MIME type; defaults to <c>application/octet-stream</c> when null.</param>
    /// <exception cref="InvalidOperationException">
    ///     Called outside an event handler, or no <see cref="IDownloadSink" /> is registered.
    /// </exception>
    public void Download(string filename, Stream stream, string? contentType = null)
    {
        EnsureInHandler();
        ArgumentException.ThrowIfNullOrEmpty(filename);
        ArgumentNullException.ThrowIfNull(stream);
        ResolveSink().Stage(filename, stream, contentType);
    }

    /// <summary>Makes the navigation this handler made replace the current history entry: <c>.Replacing()</c>.</summary>
    internal void Replace()
    {
        // A navigation handed to the session replaces already: the page that asked is not a place to go back to.
        if (OutsideADispatch)
        {
            return;
        }

        EnsureInHandler();
        _replace = true;
    }

    private IDownloadSink ResolveSink() =>
        downloadSink ?? throw new InvalidOperationException(
            "Download.File requires an IDownloadSink. Every Rask host registers one — Rask.Server via " +
            "AddRask(), WASM via WasmHostBuilder — so reaching this means the page runs outside " +
            "a host. In a unit test, hand the page a TestDownloadSink (Rask.Testing).");

    /// <summary>
    ///     Opens the window in which navigation is legal during a page's INITIAL server render, so a
    ///     page that decides on load that the user belongs elsewhere says so with the same API it
    ///     would use from a handler.
    /// </summary>
    /// <remarks>
    ///     The host consumes the result through <see cref="TryConsumeHistory" /> and answers a real
    ///     <c>302</c> — one response, rather than a whole page the client immediately navigates away
    ///     from, and one that a cache and a crawler both understand. A separate redirect API on the
    ///     response object would have split navigation across two concepts for no gain; this is the
    ///     same <c>NavigateTo</c> everywhere.
    /// </remarks>
    internal IDisposable EnterInitialRender()
    {
        _dirty = false;
        _redirected = false;
        _redirects = 0;
        _replace = false;
        _inInitialRender = true;
        var previous = _current.Value;
        _current.Value = this;
        return new HandlerScope(this, previous);
    }

    internal IDisposable EnterHandler()
    {
        // Clear any navigation a prior handler queued but never consumed — e.g. it called
        // NavigateTo(...) and then threw before TryConsumeHistory ran. Resetting on entry (rather
        // than on scope dispose) starts each dispatch clean so a faulted handler can't leak its
        // pending nav (and _replace flag) into the next one, while still allowing the caller to
        // consume the navigation after the scope disposes.
        _dirty = false;
        _handedOverInWalk = false;
        _redirected = false;
        _redirects = 0;
        _replace = false;
        _inHandler = true;
        var previous = _current.Value;
        _current.Value = this;
        return new HandlerScope(this, previous);
    }

    internal bool TryConsumeHistory(out string url, out bool replace)
    {
        if (!_dirty)
        {
            url = string.Empty;
            replace = false;
            return false;
        }

        url = BuildUrl(routeState);
        replace = _replace;
        _dirty = false;
        _redirected = false;
        _replace = false;
        return true;
    }

    /// <summary>
    ///     Whether a page navigated while it was being rendered — from <c>OnMount</c>, <c>OnUpdated</c> or
    ///     <c>Render</c> — so what was just rendered is a page the reader is not to see.
    /// </summary>
    /// <remarks>
    ///     Only while the dispatch that rendered it is still open: a redirect its host never took — the handler
    ///     threw first — must not hold back the renders that come after it.
    /// </remarks>
    internal bool RedirectPending => (_redirected && (_inHandler || _inInitialRender)) || _handedOverInWalk;

    /// <summary>Whether a navigation has been made that no host has taken yet.</summary>
    internal bool NavigationPending => _dirty;

    /// <summary>
    ///     Takes the navigation a page made while it was being rendered, for the host to render its destination
    ///     instead: the live counterpart of the <c>302</c> the first request answers.
    /// </summary>
    /// <remarks>
    ///     Counted, so that the eleventh page in a row to redirect throws where it navigates rather than have two
    ///     pages that send the reader to each other hold the session for ever.
    /// </remarks>
    internal bool TryConsumeRedirect(out string url, out bool replace)
    {
        if (!_redirected)
        {
            url = string.Empty;
            replace = false;
            return false;
        }

        _redirects++;
        return TryConsumeHistory(out url, out replace);
    }

    // The walk itself, on the thread running it: where Render and the synchronous part of a hook are. A hook's
    // continuation still carries the walk's context after an await, but is no longer inside it.
    private static bool InRenderWalk =>
        LiveRenderContext.CurrentSync is { } walk && ReferenceEquals(walk, LiveRenderContext.Current);

    private void Navigated()
    {
        _dirty = true;
        _redirected |= InRenderWalk;
    }

    // The work a session is doing with a navigation it was handed: it runs here, as itself.
    [ThreadStatic] private static bool _applying;

    // A hook past its first await, or a page rendered with no dispatch behind it (a reconnect, a render something
    // in the background asked for): nothing on this flow will take the navigation, so the session is handed it.
    // The first request is the exception — its response is still open, and the navigation becomes its 302.
    private bool OutsideADispatch =>
        !_applying
        && !_inInitialRender
        && LiveRenderContext.Current is not null
        && !(InRenderWalk && _inHandler && ReferenceEquals(_current.Value, this));

    /// <summary>
    ///     Hands a navigation no dispatch is waiting for to the session, which makes it in turn with its event
    ///     handlers: behind the destination's guard, replacing the address of the page that asked.
    /// </summary>
    /// <returns><c>true</c> when the caller has nothing left to do.</returns>
    private bool HandedToTheSession(Action navigate)
    {
        if (!OutsideADispatch || LiveRenderContext.Current?.Handle is not { } session)
        {
            return false;
        }

        // The reader has already gone elsewhere: where this page wanted to send them no longer matters.
        var asking = (SynchronizationContext.Current as LifecycleSyncContext)?.Component;
        if (asking is { IsTornDown: true })
        {
            return true;
        }

        RefuseALoop();
        var hops = _redirects + 1;
        if (!session.TryNavigate(() => Apply(navigate, asking, hops)))
        {
            return false;
        }

        // What this walk renders is a page the reader is not to see: the session's own render follows.
        _handedOverInWalk |= InRenderWalk;
        return true;
    }

    private void Apply(Action navigate, Component? asking, int hops)
    {
        if (asking is { IsTornDown: true })
        {
            return;
        }

        _redirects = hops;
        AsTheSession(navigate);
        _replace = true;
    }

    private static void AsTheSession(Action navigate)
    {
        _applying = true;
        try
        {
            navigate();
        }
        finally
        {
            _applying = false;
        }
    }

    private void BeforeNavigating()
    {
        EnsureInHandler();
        if (InRenderWalk)
        {
            RefuseALoop();
        }
    }

    private void RefuseALoop()
    {
        if (_redirects >= MaxRedirects)
        {
            throw new InvalidOperationException(
                $"Too many redirects: {MaxRedirects} pages in a row each sent the reader on as they mounted, and " +
                $"'{routeState.Path}' is doing it again. Two pages that redirect to each other never settle: " +
                "give one of them a condition under which it shows itself. See docs/routing.md.");
        }
    }

    private void EnsureInHandler()
    {
        if (!_inHandler && !_inInitialRender)
        {
            throw new InvalidOperationException(
                "This runs only while a session is handling something: in an event handler (Button.OnClick(…)), " +
                "or — for Go() — in a page's lifecycle hooks (OnMount, OnUpdated). Here nothing is being handled, " +
                "so there is no reader to move and no response to write to. See docs/routing.md.");
        }
    }

    private static Dictionary<string, StringValues> ToDictionary(IQueryCollection query)
    {
        var d = new Dictionary<string, StringValues>(query.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            d[kv.Key] = kv.Value;
        }

        return d;
    }

    private static QueryCollection BuildCollection(IEnumerable<KeyValuePair<string, string?>> query)
    {
        var d = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            if (kv.Value is null)
            {
                continue;
            }

            d[kv.Key] = d.TryGetValue(kv.Key, out var existing)
                ? StringValues.Concat(existing, kv.Value)
                : kv.Value;
        }

        return new QueryCollection(d);
    }

    internal static string BuildUrl(RouteState rs)
    {
        if (rs.Query.Count == 0)
        {
            return rs.Path;
        }

        return QueryString.Build(rs.Path, rs.Query);
    }

    // Restores the previous ambient navigator rather than clearing it, so a nested EnterHandler (the
    // Server dispatch wraps navigator and authSignIn scopes, and tests re-enter) unwinds correctly.
    private sealed class HandlerScope(Navigator nav, Navigator? previous) : IDisposable
    {
        public void Dispose()
        {
            nav._inHandler = false;
            nav._inInitialRender = false;
            _current.Value = previous;
        }
    }
}
