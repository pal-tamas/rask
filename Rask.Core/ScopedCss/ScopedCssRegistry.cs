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

    internal static bool TryRegister(Component instance, out string scopeId)
    {
        var type = instance.GetType();
        var css = instance.Css;
        var changed = false;
        lock (_lock)
        {
            if (_entries.TryGetValue(type, out var existing))
            {
                if (string.Equals(existing.Source, css, StringComparison.Ordinal))
                {
                    scopeId = existing.ScopeId;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(css))
                {
                    _entries.Remove(type);
                    _order.Remove(type);
                    InvalidateBundle();
                    scopeId = string.Empty;
                    changed = true;
                }
                else
                {
                    scopeId = existing.ScopeId;
                    var rewritten = CssScoper.Rewrite(css, scopeId);
                    _entries[type] = new Entry(scopeId, rewritten, css);
                    InvalidateBundle();
                    changed = true;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(css))
                {
                    scopeId = string.Empty;
                    return false;
                }

                scopeId = CssScoper.ScopeIdFor(type);
                var rewritten = CssScoper.Rewrite(css, scopeId);
                _entries[type] = new Entry(scopeId, rewritten, css!);
                _order.Add(type);
                InvalidateBundle();
                changed = true;
            }
        }

        if (changed)
        {
            BundleChanged?.Invoke();
        }

        return !string.IsNullOrEmpty(scopeId);
    }

    public static void Invalidate(Type componentType)
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

    public static (string Css, string Hash) GetBundle()
    {
        lock (_lock)
        {
            EnsureBundle();
            return (_cachedBundle ?? string.Empty, _cachedHash ?? "empty");
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
