using Microsoft.Extensions.Primitives;

namespace Rask.Core.Routing;

public sealed class Navigator(RouteState routeState, IDownloadSink? downloadSink = null)
{
    private bool _dirty;
    private bool _inHandler;
    private bool _replace;

    public void Navigate(RouteUrl url, bool replace = false)
    {
        EnsureInHandler();
        routeState.Path = url.Path;
        routeState.Query = string.IsNullOrEmpty(url.QueryString)
            ? QueryCollection.Empty
            : QueryString.Parse(url.QueryString);
        _replace = replace;
        _dirty = true;
    }

    public void Navigate(string path, bool replace = false)
    {
        EnsureInHandler();
        ArgumentNullException.ThrowIfNull(path);
        routeState.Path = path;
        routeState.Query = QueryCollection.Empty;
        _replace = replace;
        _dirty = true;
    }

    public void Navigate(string path, IEnumerable<KeyValuePair<string, string?>> query, bool replace = false)
    {
        EnsureInHandler();
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        routeState.Path = path;
        routeState.Query = BuildCollection(query);
        _replace = replace;
        _dirty = true;
    }

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

    public void RemoveQuery(string key)
    {
        EnsureInHandler();
        ArgumentNullException.ThrowIfNull(key);
        var dict = ToDictionary(routeState.Query);
        dict.Remove(key);
        routeState.Query = new QueryCollection(dict);
        _dirty = true;
    }

    public void ClearQuery()
    {
        EnsureInHandler();
        routeState.Query = QueryCollection.Empty;
        _dirty = true;
    }

    public void Download(string filename, byte[] bytes, string? contentType = null)
    {
        EnsureInHandler();
        ArgumentException.ThrowIfNullOrEmpty(filename);
        ArgumentNullException.ThrowIfNull(bytes);
        ResolveSink().Stage(filename, bytes, contentType);
    }

    public void Download(string filename, Stream stream, string? contentType = null)
    {
        EnsureInHandler();
        ArgumentException.ThrowIfNullOrEmpty(filename);
        ArgumentNullException.ThrowIfNull(stream);
        ResolveSink().Stage(filename, stream, contentType);
    }

    private IDownloadSink ResolveSink() =>
        downloadSink ?? throw new InvalidOperationException(
            "Navigator.Download requires an IDownloadSink. Rask.Server registers one via AddRask(); " +
            "WASM hosts get one from WasmHostBuilder. If you're in a unit test, register a fake.");

    internal IDisposable EnterHandler()
    {
        // Clear any navigation a prior handler queued but never consumed — e.g. it called
        // Navigate(...) and then threw before TryConsumeHistory ran. Resetting on entry (rather
        // than on scope dispose) starts each dispatch clean so a faulted handler can't leak its
        // pending nav (and _replace flag) into the next one, while still allowing the caller to
        // consume the navigation after the scope disposes.
        _dirty = false;
        _replace = false;
        _inHandler = true;
        return new HandlerScope(this);
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
                "Navigator can only be used from event handlers. " +
                "Calling it during component Render() or initial GET is not supported.");
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

    private sealed class HandlerScope(Navigator nav) : IDisposable
    {
        public void Dispose() => nav._inHandler = false;
    }
}
