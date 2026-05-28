using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Core.ScopedCss;

namespace Rask.Core.ScopedAssets;

/// <summary>
///     Per-component, content-addressed CSS/JS registry. Each registered (Type, AssetKind)
///     pair maps to a 12-hex content hash; the framework emits one
///     <c>&lt;link href="/_rask/a/{hash}.css"&gt;</c> or
///     <c>&lt;script src="/_rask/a/{hash}.js" defer&gt;</c> per mounted component, and the
///     host endpoint serves the bytes by hash with <c>Cache-Control: immutable</c>.
///     <para>
///         Two component types whose rewritten content is byte-equal share the same hash —
///         the registry refcounts entries by hash so unregistering one type does not drop
///         an entry another type still references.
///     </para>
/// </summary>
public static class ScopedAssetRegistry
{
    /// <summary>
    ///     Length of the lowercase-hex content hash used in <c>/_rask/a/{hash}.{ext}</c>
    ///     URLs. 12 hex chars = 48 bits — collision probability across 10k components
    ///     is ~ 1.8e-7. Host endpoint validators enforce this length.
    /// </summary>
    public const int HashHexLength = 12;

    private static readonly object _lock = new();

    private static readonly Dictionary<Type, string> _cssHashByType = new();
    private static readonly Dictionary<Type, string> _jsHashByType = new();
    private static readonly Dictionary<Type, string> _scopeIdByType = new();

    private static readonly Dictionary<string, AssetEntry> _cssByHash =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<string, AssetEntry> _jsByHash =
        new(StringComparer.Ordinal);

    private static readonly Regex _exportStrip =
        new(@"(^|\n)\s*export\s+(default\s+)?(?=(function|const|let|var)\b)",
            RegexOptions.Compiled);

    private static readonly Regex _exportedFunctionNames =
        new(@"(^|\n)\s*export\s+(?:default\s+)?function\s+(\w+)\s*\(",
            RegexOptions.Compiled);

    public static event Action<Type, AssetKind>? AssetChanged;

    /// <summary>
    ///     Registers (or replaces) the scoped CSS source for a component type. Called by
    ///     generator-emitted module initializers and by the hot-reload handler. Whitespace-only
    ///     source acts as <see cref="UnregisterCss" />. Re-registration with content that
    ///     produces the same hash is a no-op (no event raised).
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="componentType" /> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="componentType" /> is an open generic.</exception>
    public static void RegisterCss(Type componentType, string source)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (componentType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                "Open generic types cannot be registered (no stable scope id). " +
                $"Got: {componentType.FullName}.",
                nameof(componentType));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            UnregisterCss(componentType);
            return;
        }

        var scopeId = CssScoper.ScopeIdFor(componentType);
        var rewritten = CssScoper.Rewrite(source, scopeId);
        if (string.IsNullOrEmpty(rewritten))
        {
            UnregisterCss(componentType);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(rewritten);
        var hash = ComputeHash(bytes);
        var changed = false;

        lock (_lock)
        {
            if (_cssHashByType.TryGetValue(componentType, out var existing))
            {
                if (existing == hash)
                {
                    // Same content as before — preserve no-event semantics.
                    return;
                }

                DecrementRefLocked(_cssByHash, existing);
            }

            _cssHashByType[componentType] = hash;
            _scopeIdByType[componentType] = scopeId;
            IncrementOrInsertLocked(_cssByHash, hash, bytes);
            changed = true;
        }

        if (changed)
        {
            AssetChanged?.Invoke(componentType, AssetKind.Css);
        }
    }

    /// <summary>
    ///     Registers (or replaces) the scoped JS source for a component type. Wraps the
    ///     ES-module source in an IIFE assigned to <c>window.Rask[{TypeName}]</c>.
    ///     Whitespace-only source acts as <see cref="UnregisterJs" />.
    /// </summary>
    public static void RegisterJs(Type componentType, string source)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (componentType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                "Open generic types cannot be registered. " +
                $"Got: {componentType.FullName}.",
                nameof(componentType));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            UnregisterJs(componentType);
            return;
        }

        var wrapped = WrapModule(componentType.Name, source);
        var bytes = Encoding.UTF8.GetBytes(wrapped);
        var hash = ComputeHash(bytes);
        var changed = false;

        lock (_lock)
        {
            if (_jsHashByType.TryGetValue(componentType, out var existing))
            {
                if (existing == hash)
                {
                    return;
                }

                DecrementRefLocked(_jsByHash, existing);
            }

            _jsHashByType[componentType] = hash;
            IncrementOrInsertLocked(_jsByHash, hash, bytes);
            changed = true;
        }

        if (changed)
        {
            AssetChanged?.Invoke(componentType, AssetKind.Js);
        }
    }

    public static void UnregisterCss(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        bool changed;
        lock (_lock)
        {
            if (!_cssHashByType.TryGetValue(componentType, out var hash))
            {
                return;
            }

            _cssHashByType.Remove(componentType);
            _scopeIdByType.Remove(componentType);
            DecrementRefLocked(_cssByHash, hash);
            changed = true;
        }

        if (changed)
        {
            AssetChanged?.Invoke(componentType, AssetKind.Css);
        }
    }

    public static void UnregisterJs(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        bool changed;
        lock (_lock)
        {
            if (!_jsHashByType.TryGetValue(componentType, out var hash))
            {
                return;
            }

            _jsHashByType.Remove(componentType);
            DecrementRefLocked(_jsByHash, hash);
            changed = true;
        }

        if (changed)
        {
            AssetChanged?.Invoke(componentType, AssetKind.Js);
        }
    }

    /// <summary>
    ///     Clears every entry (CSS + JS). Intended for tests; production hot-reload uses
    ///     the per-kind <see cref="InvalidateAllCss" /> / <see cref="InvalidateAllJs" />
    ///     so a CSS-only refresh doesn't blow away JS entries (and vice versa). Does not
    ///     raise <see cref="AssetChanged" /> — callers needing a coarse "cleared" signal
    ///     observe via their own hook.
    /// </summary>
    public static void InvalidateAll()
    {
        lock (_lock)
        {
            _cssHashByType.Clear();
            _jsHashByType.Clear();
            _scopeIdByType.Clear();
            _cssByHash.Clear();
            _jsByHash.Clear();
        }
    }

    /// <summary>
    ///     Drops every CSS entry (and its scope-id mapping); leaves JS entries untouched.
    ///     Called by the CSS hot-reload handler so a deleted <c>.css</c> sibling actually
    ///     disappears from the registry — <c>RegisterCss</c> re-runs from RefreshAll over
    ///     surviving pairs only, and would never visit the deleted slot.
    /// </summary>
    public static void InvalidateAllCss()
    {
        lock (_lock)
        {
            _cssHashByType.Clear();
            _scopeIdByType.Clear();
            _cssByHash.Clear();
        }
    }

    /// <summary>Drops every JS entry; leaves CSS entries untouched.</summary>
    public static void InvalidateAllJs()
    {
        lock (_lock)
        {
            _jsHashByType.Clear();
            _jsByHash.Clear();
        }
    }

    /// <summary>
    ///     Yields every registered asset entry as <c>(hash, kind, utf8Bytes)</c>. Used by
    ///     the publish-time bake task (<c>Rask.Wasm.Tasks.BakeScopedAssetsTask</c>) to
    ///     materialise <c>/_rask/a/{hash}.{ext}</c> files into the published WASM
    ///     AppBundle so a static-file-only host like <c>WasmAppHost</c> serves the same
    ///     bytes the in-process endpoint would. Returns a snapshot copy so iteration is
    ///     safe under concurrent registration; the byte spans alias the registry's
    ///     pooled storage so consumers must not retain or mutate them beyond write-out.
    /// </summary>
    public static IEnumerable<EnumeratedEntry> EnumerateAll()
    {
        var snapshot = new List<EnumeratedEntry>();
        lock (_lock)
        {
            foreach (var kv in _cssByHash)
            {
                snapshot.Add(new EnumeratedEntry(kv.Key, AssetKind.Css, kv.Value.Utf8));
            }

            foreach (var kv in _jsByHash)
            {
                snapshot.Add(new EnumeratedEntry(kv.Key, AssetKind.Js, kv.Value.Utf8));
            }
        }

        return snapshot;
    }

    public readonly record struct EnumeratedEntry(string Hash, AssetKind Kind, ReadOnlyMemory<byte> Utf8);

    /// <summary>
    ///     Looks up the asset hash for a component's CSS. Returns false (with empty out)
    ///     when the type has no scoped CSS registered. Called by head emission to decide
    ///     whether to emit a <c>&lt;link&gt;</c> tag for the type.
    /// </summary>
    public static bool TryGetCss(Type componentType, out string hash)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        lock (_lock)
        {
            if (_cssHashByType.TryGetValue(componentType, out var v))
            {
                hash = v;
                return true;
            }
        }

        hash = string.Empty;
        return false;
    }

    /// <summary>
    ///     Looks up the asset hash for a component's JS.
    /// </summary>
    public static bool TryGetJs(Type componentType, out string hash)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        lock (_lock)
        {
            if (_jsHashByType.TryGetValue(componentType, out var v))
            {
                hash = v;
                return true;
            }
        }

        hash = string.Empty;
        return false;
    }

    /// <summary>
    ///     Returns the CSS scope id (<c>r-{8 hex}</c>) for a component, or false when the
    ///     type has no scoped CSS registered. Called per-render by <c>HtmlSerializer</c> /
    ///     <c>LiveRenderContext.PushScope</c> to stamp <c>data-r-xxxx</c> on body elements.
    /// </summary>
    public static bool TryGetScopeId(Type componentType, out string scopeId)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        lock (_lock)
        {
            if (_scopeIdByType.TryGetValue(componentType, out var v))
            {
                scopeId = v;
                return true;
            }
        }

        scopeId = string.Empty;
        return false;
    }

    /// <summary>
    ///     Looks up the pre-encoded bytes for an asset by hash and kind. Returns null when
    ///     the hash is unknown for the requested kind (kind mismatch is intentional — a CSS
    ///     hash queried as JS returns null, prevents cross-type confusion).
    /// </summary>
    public static AssetBytes? GetByHash(string hash, AssetKind kind)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        var bucket = kind == AssetKind.Css ? _cssByHash : _jsByHash;
        lock (_lock)
        {
            if (bucket.TryGetValue(hash, out var entry))
            {
                return new AssetBytes(entry.Utf8, entry.Etag);
            }
        }

        return null;
    }

    internal static int CssEntryCount
    {
        get
        {
            lock (_lock)
            {
                return _cssByHash.Count;
            }
        }
    }

    internal static int JsEntryCount
    {
        get
        {
            lock (_lock)
            {
                return _jsByHash.Count;
            }
        }
    }

    private static void IncrementOrInsertLocked(
        Dictionary<string, AssetEntry> bucket, string hash, byte[] bytes)
    {
        if (bucket.TryGetValue(hash, out var entry))
        {
            entry.RefCount++;
            return;
        }

        bucket[hash] = new AssetEntry(bytes, "\"" + hash + "\"") { RefCount = 1 };
    }

    private static void DecrementRefLocked(Dictionary<string, AssetEntry> bucket, string hash)
    {
        if (!bucket.TryGetValue(hash, out var entry))
        {
            return;
        }

        entry.RefCount--;
        if (entry.RefCount <= 0)
        {
            bucket.Remove(hash);
        }
    }

    private static string ComputeHash(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        // 12 hex chars = 48 bits of entropy. Collision probability across 10k components
        // is ~ 10000^2 / 2 / 2^48 = 1.8e-7 (negligible). Lowercase enforced so URL routing
        // can apply a strict ^[0-9a-f]+$ constraint that prevents cache fragmentation from
        // case-variant requests.
        Span<char> chars = stackalloc char[HashHexLength];
        for (var i = 0; i < HashHexLength / 2; i++)
        {
            var b = hash[i];
            chars[i * 2] = ToLowerHex(b >> 4);
            chars[i * 2 + 1] = ToLowerHex(b & 0xF);
        }

        return new string(chars);
    }

    private static char ToLowerHex(int nibble)
        => (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);

    private static string WrapModule(string typeName, string source)
    {
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

    public readonly record struct AssetBytes(ReadOnlyMemory<byte> Utf8, string Etag);

    private sealed class AssetEntry
    {
        public AssetEntry(byte[] utf8, string etag)
        {
            Utf8 = utf8;
            Etag = etag;
        }

        public byte[] Utf8 { get; }
        public string Etag { get; }
        public int RefCount;
    }
}
