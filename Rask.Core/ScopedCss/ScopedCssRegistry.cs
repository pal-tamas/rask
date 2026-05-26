using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Rask.Core.ScopedCss;

public static class ScopedCssRegistry
{
    private static readonly object _lock = new();
    private static readonly Dictionary<Type, Entry> _entries = new();
    private static readonly List<Type> _order = new();
    private static string? _cachedBundle;

    private static string? _cachedHash;

    // Lock-free read cache for the hot path: every user-component render hits
    // TryRegister, which previously took _lock + dictionary lookup. Most calls are
    // pure reads against a stable type→scopeId mapping, so a ConcurrentDictionary
    // amortises the lookup. Hot-reload (RegisterType/UnregisterType) bumps the
    // dictionary entries in lockstep so a fresh scope id propagates immediately.
    // Sentinel value "" represents "type has no registered scope" — distinguishes
    // the negative cache hit from "not yet observed".
    private static readonly ConcurrentDictionary<Type, string> _scopeIdCache = new();

    // Cached UTF-8 encoding and pre-quoted ETag for the served path. The bundle string is
    // computed on first GetBundle() or GetBundleUtf8() call after an invalidation; the
    // bytes/etag are computed lazily on first GetBundleUtf8(). All three fields share the
    // same _lock and are invalidated together by InvalidateBundle.
    private static byte[]? _cachedBundleUtf8;
    private static string? _cachedEtag;

    public static string? CurrentHash
    {
        get
        {
            lock (_lock)
            {
                if (_order.Count == 0)
                {
                    return null;
                }

                EnsureBundle();
                return _cachedHash;
            }
        }
    }

    internal static int EntryCount
    {
        get
        {
            lock (_lock)
            {
                return _order.Count;
            }
        }
    }

    public static event Action? BundleChanged;

    /// <summary>
    ///     Returns the scope id stamped on body elements rendered under a component of the
    ///     given type, or null/empty when no CSS has been registered for that type. Called
    ///     from <see cref="Live.LiveRenderContext.PushScope" /> on every render.
    /// </summary>
    internal static bool TryRegister(Type componentType, out string scopeId)
    {
        // Lock-free fast path. The cache is populated lazily on miss; subsequent
        // renders of the same type skip the _lock entirely.
        if (_scopeIdCache.TryGetValue(componentType, out var cached))
        {
            if (cached.Length == 0)
            {
                scopeId = string.Empty;
                return false;
            }

            scopeId = cached;
            return true;
        }

        lock (_lock)
        {
            if (_entries.TryGetValue(componentType, out var existing))
            {
                scopeId = existing.ScopeId;
                _scopeIdCache[componentType] = scopeId;
                return true;
            }
        }

        // Negative cache: empty string means "no scope for this type". Register/Unregister
        // invalidate this entry so a hot-reload that adds CSS to a component lights up
        // on the next render.
        _scopeIdCache[componentType] = string.Empty;
        scopeId = string.Empty;
        return false;
    }

    /// <summary>
    ///     Registers (or replaces) the CSS for a component type. Called by generator-emitted
    ///     module initializers and by the hot-reload handler when the generated source for a
    ///     `.css` sibling is re-emitted. No-op when <paramref name="css" /> equals the previously
    ///     registered source (same string, same hash, same scope id).
    /// </summary>
    public static void RegisterType(Type componentType, string css)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            UnregisterType(componentType);
            return;
        }

        bool changed;
        lock (_lock)
        {
            if (_entries.TryGetValue(componentType, out var existing))
            {
                if (string.Equals(existing.Source, css, StringComparison.Ordinal))
                {
                    return;
                }

                var rewritten = CssScoper.Rewrite(css, existing.ScopeId);
                _entries[componentType] = new Entry(existing.ScopeId, rewritten, css);
                InvalidateBundle();
                changed = true;
            }
            else
            {
                var scopeId = CssScoper.ScopeIdFor(componentType);
                var rewritten = CssScoper.Rewrite(css, scopeId);
                _entries[componentType] = new Entry(scopeId, rewritten, css);
                _order.Add(componentType);
                InvalidateBundle();
                changed = true;
            }
        }

        // Invalidate the read cache so the next TryRegister picks up the new (or
        // re-scoped) entry. Removes both positive- and negative-cache entries.
        _scopeIdCache.TryRemove(componentType, out _);

        if (changed)
        {
            BundleChanged?.Invoke();
        }
    }

    /// <summary>
    ///     Removes the entry for a component type. Used by hot-reload (when a `.css` sibling
    ///     is deleted) and by tests that need to clean up registrations.
    /// </summary>
    public static void UnregisterType(Type componentType)
    {
        bool changed;
        lock (_lock)
        {
            changed = _entries.Remove(componentType);
            if (changed)
            {
                _order.Remove(componentType);
                InvalidateBundle();
            }
        }

        _scopeIdCache.TryRemove(componentType, out _);

        if (changed)
        {
            BundleChanged?.Invoke();
        }
    }

    public static void Invalidate(Type componentType) => UnregisterType(componentType);

    public static void InvalidateAll()
    {
        bool changed;
        lock (_lock)
        {
            changed = _entries.Count > 0;
            _entries.Clear();
            _order.Clear();
            InvalidateBundle();
        }

        _scopeIdCache.Clear();

        if (changed)
        {
            BundleChanged?.Invoke();
        }
    }

    public static (string Css, string Hash) GetBundle()
    {
        lock (_lock)
        {
            EnsureBundle();
            return (_cachedBundle ?? string.Empty, _cachedHash ?? "empty");
        }
    }

    /// <summary>
    ///     UTF-8 byte view of <see cref="GetBundle" /> plus a pre-formatted ETag header value
    ///     (already wrapped in double quotes). Both are cached alongside the string bundle and
    ///     invalidated together — the served endpoint can write the bytes straight to the
    ///     response body without re-encoding UTF-8 per request.
    /// </summary>
    public static (ReadOnlyMemory<byte> Css, string Etag) GetBundleUtf8()
    {
        lock (_lock)
        {
            EnsureBundle();
            if (_cachedBundleUtf8 is null || _cachedEtag is null)
            {
                _cachedBundleUtf8 = Encoding.UTF8.GetBytes(_cachedBundle ?? string.Empty);
                _cachedEtag = $"\"{_cachedHash ?? "empty"}\"";
            }

            return (_cachedBundleUtf8, _cachedEtag);
        }
    }

    private static void EnsureBundle()
    {
        if (_cachedBundle is not null)
        {
            return;
        }

        if (_order.Count == 0)
        {
            _cachedBundle = string.Empty;
            _cachedHash = "empty";
            return;
        }

        var sb = new StringBuilder();
        foreach (var t in _order)
        {
            sb.Append(_entries[t].RewrittenCss);
        }

        _cachedBundle = sb.ToString();
        _cachedHash = ComputeShortHash(_cachedBundle);
    }

    private static void InvalidateBundle()
    {
        _cachedBundle = null;
        _cachedHash = null;
        _cachedBundleUtf8 = null;
        _cachedEtag = null;
    }

    private static string ComputeShortHash(string s)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(s), hash);
        var sb = new StringBuilder(8);
        for (var i = 0; i < 4; i++)
        {
            sb.Append(hash[i].ToString("x2"));
        }

        return sb.ToString();
    }

    private readonly record struct Entry(string ScopeId, string RewrittenCss, string Source);
}
