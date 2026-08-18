using Microsoft.Extensions.Primitives;

namespace Rask.Core.Routing;

/// <summary>
///     Imperative client-side navigation and query-string mutation, plus file downloads.
///     Inject it through a component constructor (<c>public MyPage(Navigator nav)</c>) and call
///     it from <b>event handlers only</b>.
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
public sealed class Navigator(RouteState routeState, IDownloadSink? downloadSink = null)
{
    // The navigator of the handler currently running on this async flow. Published by EnterHandler and
    // cleared when that scope disposes, so it is set for exactly the window in which navigation is legal —
    // the same window _inHandler describes. It exists so the generated `SomePage.Go(...)` static extensions
    // can navigate without a receiver to inject through: they have no instance and no DI to reach.
    //
    // AsyncLocal (not ThreadStatic) because a handler may await: the value has to flow into continuations
    // that resume on another pool thread. Per-session correctness comes from the DI registration — Server
    // registers Navigator per session scope, WASM/Native as a singleton because a host owns one session —
    // so whichever navigator a dispatch entered is that dispatch's own.
    private static readonly AsyncLocal<Navigator?> _current = new();

    private bool _dirty;
    private bool _inHandler;
    private bool _replace;

    /// <summary>
    ///     The <see cref="Navigator" /> for the event handler currently running, or <c>null</c> outside one.
    ///     The generated <c>SomePage.Go(...)</c> helpers use this; injecting <see cref="Navigator" /> through a
    ///     component constructor is the equivalent explicit route.
    /// </summary>
    public static Navigator? Current => _current.Value;

    /// <summary>
    ///     <see cref="Current" />, or a throw with the same actionable message the instance methods raise when
    ///     used outside an event handler. Public because the generated <c>SomePage.Go(...)</c> helpers are
    ///     compiled into the consumer's assembly and call it; prefer injecting <see cref="Navigator" /> through
    ///     a component constructor in code you write by hand.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static Navigator RequireCurrent() =>
        _current.Value ?? throw new InvalidOperationException(
            "Navigation can only run from event handlers (e.g. Button(OnClick: ...)). " +
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
        EnsureInHandler();
        routeState.Path = url.Path;
        routeState.Query = string.IsNullOrEmpty(url.QueryString)
            ? QueryCollection.Empty
            : QueryString.Parse(url.QueryString);
        _replace = replace;
        _dirty = true;
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
        EnsureInHandler();
        ArgumentNullException.ThrowIfNull(path);
        routeState.Path = path;
        routeState.Query = QueryCollection.Empty;
        _replace = replace;
        _dirty = true;
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
        EnsureInHandler();
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        routeState.Path = path;
        routeState.Query = BuildCollection(query);
        _replace = replace;
        _dirty = true;
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
        EnsureInHandler();
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
        _dirty = true;
    }

    /// <summary>
    ///     Sets or updates several query parameters on the current path in one update. Pairs with a
    ///     <c>null</c> value remove that key; all other params are preserved.
    /// </summary>
    /// <param name="values">Parameters to set or remove.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void SetQuery(params KeyValuePair<string, string?>[] values)
    {
        EnsureInHandler();
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
        _dirty = true;
    }

    /// <summary>Removes a single query parameter from the current path. Missing keys are a no-op.</summary>
    /// <param name="key">Query parameter name (case-insensitive).</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void RemoveQuery(string key)
    {
        EnsureInHandler();
        ArgumentNullException.ThrowIfNull(key);
        var dict = ToDictionary(routeState.Query);
        dict.Remove(key);
        routeState.Query = new QueryCollection(dict);
        _dirty = true;
    }

    /// <summary>Removes all query parameters from the current path, keeping the path.</summary>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void ClearQuery()
    {
        EnsureInHandler();
        routeState.Query = QueryCollection.Empty;
        _dirty = true;
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

    private IDownloadSink ResolveSink() =>
        downloadSink ?? throw new InvalidOperationException(
            "Navigator.Download requires an IDownloadSink. Every Rask host registers one — Rask.Server via " +
            "AddRask(), WASM via WasmHostBuilder, native via NativeAppHost — so reaching this means the " +
            "Navigator was built outside a host. If you're in a unit test, register a fake (Rask.Testing " +
            "ships TestDownloadSink).");

    internal IDisposable EnterHandler()
    {
        // Clear any navigation a prior handler queued but never consumed — e.g. it called
        // NavigateTo(...) and then threw before TryConsumeHistory ran. Resetting on entry (rather
        // than on scope dispose) starts each dispatch clean so a faulted handler can't leak its
        // pending nav (and _replace flag) into the next one, while still allowing the caller to
        // consume the navigation after the scope disposes.
        _dirty = false;
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
        _replace = false;
        return true;
    }

    private void EnsureInHandler()
    {
        if (!_inHandler)
        {
            throw new InvalidOperationException(
                "Navigator can only be used from event handlers (e.g. Button(OnClick: ...)). " +
                "It cannot run during component Render() or the initial GET. To navigate on load, " +
                "express it as a route/redirect or drive it from a lifecycle hook; to redirect an " +
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

    private static string BuildUrl(RouteState rs)
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
            _current.Value = previous;
        }
    }
}
