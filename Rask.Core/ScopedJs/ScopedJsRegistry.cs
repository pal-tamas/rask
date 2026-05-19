using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Core.ScopedCss;

namespace Rask.Core.ScopedJs;

/// <summary>
///     Per-component JS module registry. A user drops <c>Component.js</c> next to
///     <c>Component.cs</c>; the source generator emits a module initializer that calls
///     <see cref="RegisterType"/> here. The bundle is concatenated, hashed, and served
///     to the browser by the host (server: <c>/_rask/scoped.js?v={hash}</c>; WASM: inline
///     <c>&lt;script id="rask-scoped-js"&gt;</c>). The dispatcher in
///     <c>Rask.Core/Resources/rask-scoped.js</c> calls <c>mount(el)</c> / <c>unmount(el)</c>
///     against elements tagged with <c>data-rask-mount="{scopeId}"</c>.
/// </summary>
public static class ScopedJsRegistry
{
    private static readonly object _lock = new();
    private static readonly Dictionary<Type, Entry> _entries = new();
    private static readonly List<Type> _order = new();
    private static string? _cachedBundle;
    private static string? _cachedHash;
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

    public static bool IsRegistered(Type componentType)
    {
        lock (_lock)
        {
            return _entries.ContainsKey(componentType);
        }
    }

    /// <summary>
    ///     Registers (or replaces) the JS module source for a component type. Called by
    ///     generator-emitted module initializers and by the hot-reload handler when the
    ///     generated source for a <c>.js</c> sibling is re-emitted. Whitespace-only source
    ///     unregisters the type — symmetric with <see cref="ScopedCssRegistry.RegisterType"/>.
    /// </summary>
    public static void RegisterType(Type componentType, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            UnregisterType(componentType);
            return;
        }

        bool changed;
        lock (_lock)
        {
            if (_entries.TryGetValue(componentType, out var existing))
            {
                if (string.Equals(existing.Source, source, StringComparison.Ordinal))
                {
                    return;
                }

                var wrapped = WrapModule(existing.ScopeId, source);
                _entries[componentType] = new Entry(existing.ScopeId, wrapped, source);
                InvalidateBundle();
                changed = true;
            }
            else
            {
                var scopeId = CssScoper.ScopeIdFor(componentType);
                var wrapped = WrapModule(scopeId, source);
                _entries[componentType] = new Entry(scopeId, wrapped, source);
                _order.Add(componentType);
                InvalidateBundle();
                changed = true;
            }
        }

        if (changed)
        {
            BundleChanged?.Invoke();
        }
    }

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

        if (changed)
        {
            BundleChanged?.Invoke();
        }
    }

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

        if (changed)
        {
            BundleChanged?.Invoke();
        }
    }

    public static (string Js, string Hash) GetBundle()
    {
        lock (_lock)
        {
            EnsureBundle();
            return (_cachedBundle ?? string.Empty, _cachedHash ?? "empty");
        }
    }

    public static (ReadOnlyMemory<byte> Js, string Etag) GetBundleUtf8()
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
            sb.Append(_entries[t].WrappedJs);
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

    // The author writes idiomatic ES-module syntax (`export function rendered(el) { ... }`).
    // We strip the leading `export` keyword on function/const/let/var declarations, then
    // wrap the body in a Rask.scoped.register call that returns whatever `rendered` the
    // author defined. Teardown is expressed by `rendered` returning a cleanup function —
    // mirrors React's useEffect contract.
    private static readonly Regex _exportStrip =
        new(@"(^|\n)\s*export\s+(default\s+)?(?=(function|const|let|var)\b)",
            RegexOptions.Compiled);

    private static string WrapModule(string scopeId, string source)
    {
        var stripped = _exportStrip.Replace(source, "$1");
        var sb = new StringBuilder(stripped.Length + 128);
        sb.Append("Rask.scoped.register(\"").Append(scopeId).Append("\", function () {\n");
        sb.Append(stripped);
        if (!stripped.EndsWith('\n'))
        {
            sb.Append('\n');
        }

        sb.Append("    return typeof rendered === 'function' ? rendered : undefined;\n");
        sb.Append("});\n");
        return sb.ToString();
    }

    private readonly record struct Entry(string ScopeId, string WrappedJs, string Source);
}
