using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Rask.Core.ScopedJs;

/// <summary>
///     Per-component JS module registry. A user drops <c>Component.js</c> next to
///     <c>Component.cs</c>; the source generator emits a module initializer that calls
///     <see cref="RegisterType" /> here. Each entry's exports are wrapped as an IIFE
///     assigned to <c>window.Rask.{TypeName}</c>; the bundle is concatenated, hashed,
///     and served to the browser by the host (server:
///     <c>/_rask/scoped.js?v={hash}</c>; WASM: inline
///     <c>&lt;script id="rask-scoped-js"&gt;</c>).
///     <para>
///         User C# code dispatches via the standard <see cref="Microsoft.JSInterop.IJSRuntime" /> —
///         e.g. <c>js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender)</c>.
///         The JS module retrieves its own elements (no auto-<c>el</c> arg); user JS
///         queries the DOM by a marker class it controls.
///     </para>
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

    // The author writes idiomatic ES-module syntax — any number of named exports:
    //   export function rendered(el, firstRender) { ... }
    //   export function highlight(el, language) { ... }
    // The wrapper strips `export` keywords (so the declarations are local) and then
    // returns an object literal mapping each exported function name to the
    // declaration in the closure. C# user code invokes individual methods by name
    // via InvokeJs("rendered", args...), routed through Rask.scoped.invoke.
    private static readonly Regex _exportStrip =
        new(@"(^|\n)\s*export\s+(default\s+)?(?=(function|const|let|var)\b)",
            RegexOptions.Compiled);

    // Locate `export function NAME(` declarations — these become methods on the
    // returned module object. Non-exported helpers stay in the closure scope.
    private static readonly Regex _exportedFunctionNames =
        new(@"(^|\n)\s*export\s+(?:default\s+)?function\s+(\w+)\s*\(",
            RegexOptions.Compiled);

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
    ///     unregisters the type — symmetric with <see cref="ScopedCssRegistry.RegisterType" />.
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

                var wrapped = WrapModule(componentType.Name, source);
                _entries[componentType] = new Entry(wrapped, source);
                InvalidateBundle();
                changed = true;
            }
            else
            {
                var wrapped = WrapModule(componentType.Name, source);
                _entries[componentType] = new Entry(wrapped, source);
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

    private static string WrapModule(string typeName, string source)
    {
        // Capture the set of exported function names BEFORE stripping the `export`
        // keyword — once stripped, the marker is gone and we can't tell exports
        // apart from helpers.
        var exportedNames = new List<string>();
        foreach (Match m in _exportedFunctionNames.Matches(source))
        {
            var name = m.Groups[2].Value;
            if (!exportedNames.Contains(name, StringComparer.Ordinal))
            {
                exportedNames.Add(name);
            }
        }

        var stripped = _exportStrip.Replace(source, "$1");
        var sb = new StringBuilder(stripped.Length + 128);
        // Wrap the module body in an IIFE assigned to `window.Rask.{TypeName}` so user
        // C# code can call into it through standard IJSRuntime against `window` —
        // e.g. `js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender)`.
        // Components with colliding short names should differentiate by namespacing
        // their exports themselves; the registry guards by `componentType` (full type)
        // but the JS-side namespace is the simple type name for ergonomics.
        sb.Append("(function () {\n");
        sb.Append("window.Rask = window.Rask || {};\n");
        sb.Append("window.Rask[\"").Append(typeName).Append("\"] = (function () {\n");
        sb.Append(stripped);
        if (!stripped.EndsWith('\n'))
        {
            sb.Append('\n');
        }

        sb.Append("    return {");
        if (exportedNames.Count == 0)
        {
            sb.Append("};\n})();\n})();\n");
            return sb.ToString();
        }

        sb.Append('\n');
        for (var i = 0; i < exportedNames.Count; i++)
        {
            var name = exportedNames[i];
            sb.Append("        ").Append(name).Append(": typeof ").Append(name)
                .Append(" === 'function' ? ").Append(name).Append(" : undefined");
            if (i < exportedNames.Count - 1)
            {
                sb.Append(',');
            }

            sb.Append('\n');
        }

        sb.Append("    };\n})();\n})();\n");
        return sb.ToString();
    }

    private readonly record struct Entry(string WrappedJs, string Source);
}
