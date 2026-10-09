using Microsoft.Extensions.Primitives;
using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>
///     The engine under <see cref="Rask.Core.Go" /> and <see cref="Rask.Core.Download" />: client-side navigation, query-string
///     changes and file downloads for one session, legal from <b>event handlers only</b>.
///     <para>
///         Every method throws <see cref="InvalidOperationException" /> if called outside an event
///         handler — e.g. during <c>Render()</c> or the initial GET. Navigation that needs to happen
///         on load belongs in a lifecycle hook that runs a handler-equivalent path, or should be
///         expressed as a route/redirect, not driven from render.
///     </para>
///     <para>
///         Changes are applied to the shared <see cref="RouteState" /> and the resulting URL is
///         pushed (or replaced) into browser history by the live runtime after the handler returns.
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
        _current.Value ?? throw new InvalidOperationException(
            "Navigation can only run from event handlers (e.g. Button.OnClick(…)). " +
            "It cannot run during component Render() or the initial GET. To navigate on load, " +
            "express it as a route/redirect or drive it from a lifecycle hook; to redirect an " +
            "unauthenticated user, use [Authorize]. See docs/routing.md.");

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
    internal bool RedirectPending => _redirected && (_inHandler || _inInitialRender);

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

    private void BeforeNavigating()
    {
        EnsureInHandler();
        var inWalk = InRenderWalk;
        if (!inWalk && !_inInitialRender && LiveRenderContext.Current is not null)
        {
            throw new InvalidOperationException(
                "A lifecycle hook navigated after an await, when its page was already on screen. Decide before the " +
                "first await (OnMount and OnUpdated may call Go() there, and the reader lands on the destination " +
                "without seeing this page), or navigate from an event handler. See docs/routing.md.");
        }

        if (inWalk && _redirects >= MaxRedirects)
        {
            throw new InvalidOperationException(
                $"Too many redirects: {MaxRedirects} pages in a row each sent the reader on as they mounted, and " +
                $"'{routeState.Path}' is doing it again. Two pages that redirect to each other never settle — " +
                "see docs/routing.md.");
        }
    }

    private void EnsureInHandler()
    {
        if (!_inHandler && !_inInitialRender)
        {
            throw new InvalidOperationException(
                "Navigator can only be used from event handlers (e.g. Button.OnClick(…)) or " +
                "during a page's initial render (Render, OnMount), where it becomes " +
                "a real HTTP redirect. It cannot run from a background render. To redirect an " +
                "unauthenticated user, use [Authorize]. See docs/routing.md.");
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
