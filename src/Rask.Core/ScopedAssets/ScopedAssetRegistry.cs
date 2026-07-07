using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Rask.Core.Live;
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

    // by-Type lookups are read once per user component per render (TryGetScopeId via
    // LiveRenderContext.PushScope) and per mounted type during head emission — the hottest
    // registry path. ConcurrentDictionary makes those reads lock-free so concurrent sessions
    // never serialize on a shared lock. Writes (register/unregister) still run inside _lock so
    // each stays atomic with the refcounted by-hash buckets below.
    private static readonly ConcurrentDictionary<Type, string> _cssHashByType = new();
    private static readonly ConcurrentDictionary<Type, string> _jsHashByType = new();
    private static readonly ConcurrentDictionary<Type, string> _scopeIdByType = new();

    private static readonly Dictionary<string, AssetEntry> _cssByHash =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<string, AssetEntry> _jsByHash =
        new(StringComparer.Ordinal);

    // Strips the leading `export ` (and an optional `default `) from a declaration so the
    // module body can run inside the IIFE. The lookahead lists `async function` alongside
    // the bare forms — without it an `export async function` keeps its `export` keyword and
    // throws a SyntaxError inside the (non-module) wrapper.
    private static readonly Regex _exportStrip =
        new(@"(^|\n)\s*export\s+(default\s+)?(?=(async\s+function|function|const|let|var)\b)",
            RegexOptions.Compiled);

    // Collects the names of exported function declarations (sync or async) so they can be
    // re-exposed on the returned object. The `async` modifier is optional and non-capturing,
    // so the name stays in group 2.
    private static readonly Regex _exportedFunctionNames =
        new(@"(^|\n)\s*export\s+(?:default\s+)?(?:async\s+)?function\s+(\w+)\s*\(",
            RegexOptions.Compiled);

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

    public static event Action<Type, AssetKind>? AssetChanged;

    // True when at least one component has registered scoped CSS (so a scope id exists to push). A
    // lock-free ConcurrentDictionary.IsEmpty check the per-component render walk uses to skip the by-type
    // scope lookup entirely on the common app that has no scoped CSS — the lookup would always miss.
    internal static bool HasAnyScopedCss => !_scopeIdByType.IsEmpty;

    private static long _version;

    /// <summary>
    ///     Monotonic mutation counter — bumped under the registry lock on every change to the
    ///     registered asset set (register, unregister, and the bulk <see cref="InvalidateAll" />
    ///     / <see cref="InvalidateAllCss" /> / <see cref="InvalidateAllJs" /> paths). Consumers
    ///     that cache derived output (the head <c>&lt;link rel="preload"&gt;</c> block) compare
    ///     against this to detect staleness cheaply, without subscribing to
    ///     <see cref="AssetChanged" /> — which intentionally does not fire on the bulk-clear
    ///     paths, so it cannot be relied on to invalidate a whole-registry projection.
    /// </summary>
    internal static long Version => Interlocked.Read(ref _version);

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
            Interlocked.Increment(ref _version);
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
            Interlocked.Increment(ref _version);
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

            _cssHashByType.TryRemove(componentType, out _);
            _scopeIdByType.TryRemove(componentType, out _);
            DecrementRefLocked(_cssByHash, hash);
            Interlocked.Increment(ref _version);
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

            _jsHashByType.TryRemove(componentType, out _);
            DecrementRefLocked(_jsByHash, hash);
            Interlocked.Increment(ref _version);
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
            Interlocked.Increment(ref _version);
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
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Drops every JS entry; leaves CSS entries untouched.</summary>
    public static void InvalidateAllJs()
    {
        lock (_lock)
        {
            _jsHashByType.Clear();
            _jsByHash.Clear();
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    ///     Yields every registered asset entry as <c>(hash, kind, utf8Bytes)</c>. Used by
    ///     the publish-time bake task (<c>Rask.Wasm.Tasks.BakeScopedAssetsTask</c>) to
    ///     materialise <c>/_rask/a/{hash}.{ext}</c> files (registered as static web assets)
    ///     into the published WASM <c>wwwroot</c> so a static-file-only host like
    ///     <c>WasmAppHost</c> serves the same bytes the in-process endpoint would. Returns a snapshot copy so iteration is
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

    // Single concatenated bundle per kind (all registered scoped CSS / JS), cached by registry
    // Version. The framework emits ONE <link>/<script> at the bundle's content-hash URL instead of
    // one tag per mounted component; the bundle is served like any other content-addressed asset
    // (GetByHash resolves it), so its URL is immutable and a static-asset host can ship it as a
    // single fingerprinted file. Rebuilt only when the registered set changes.
    private sealed record BundleEntry(long Version, bool Minified, string Hash, AssetBytes Bytes);

    private static volatile BundleEntry? _cssBundle;
    private static volatile BundleEntry? _jsBundle;

    /// <summary>
    ///     Returns the content hash of the single concatenated bundle for <paramref name="kind" />,
    ///     or empty when no asset of that kind is registered. The bundle is every registered scoped
    ///     CSS (or JS) entry concatenated in a deterministic (hash-sorted) order with a newline
    ///     separator; its bytes are addressable via <see cref="GetByHash" /> under the returned hash,
    ///     so the existing <c>/_rask/a/{hash}.{ext}</c> serving path resolves it unchanged.
    /// </summary>
    public static string GetBundleHash(AssetKind kind)
    {
        var bundle = EnsureBundle(kind);
        return bundle?.Hash ?? string.Empty;
    }

    private static BundleEntry? EnsureBundle(AssetKind kind)
    {
        var version = Version;
        // Only the CSS bundle is minified (the JS bundle is served as-is). Reading the flag here — and
        // keying the cache on it — means flipping LiveOptions.MinifyScopedAssets rebuilds the bundle
        // (its bytes, hash, and immutable URL) rather than serving a stale representation.
        var minify = kind == AssetKind.Css && LiveOptions.MinifyScopedAssets == true;
        var cached = kind == AssetKind.Css ? _cssBundle : _jsBundle;
        if (cached is not null && cached.Version == version && cached.Minified == minify)
        {
            return cached.Hash.Length == 0 ? null : cached;
        }

        // Snapshot + concatenate under the lock so the bundle is atomic w.r.t. the by-hash buckets.
        // Hash-sorted order makes the bundle bytes (and therefore its hash + the emitted URL)
        // deterministic regardless of registration order — two builds of the same component set
        // produce byte-identical bundles, so the immutable URL stays stable across deployments.
        var bucket = kind == AssetKind.Css ? _cssByHash : _jsByHash;
        byte[] bytes;
        lock (_lock)
        {
            if (bucket.Count == 0)
            {
                var empty = new BundleEntry(version, minify, string.Empty, default);
                if (kind == AssetKind.Css) { _cssBundle = empty; } else { _jsBundle = empty; }
                return null;
            }

            var ordered = new List<KeyValuePair<string, AssetEntry>>(bucket);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            using var ms = new MemoryStream();
            foreach (var kv in ordered)
            {
                ms.Write(kv.Value.Utf8, 0, kv.Value.Utf8.Length);
                ms.WriteByte((byte)'\n');
            }

            bytes = ms.ToArray();
        }

        // Minify the fully-concatenated CSS once per rebuild, before hashing, so the digest + immutable
        // URL + brotli/gzip caches all key off the minified bytes (never a double representation).
        if (minify)
        {
            bytes = CssMinifier.MinifyUtf8(bytes);
        }

        var hash = ComputeHash(bytes);
        var entry = new BundleEntry(version, minify, hash, new AssetBytes(bytes, "\"" + hash + "\""));
        if (kind == AssetKind.Css) { _cssBundle = entry; } else { _jsBundle = entry; }
        return entry;
    }

    /// <summary>
    ///     Looks up the asset hash for a component's CSS. Returns false (with empty out)
    ///     when the type has no scoped CSS registered. Called by head emission to decide
    ///     whether to emit a <c>&lt;link&gt;</c> tag for the type.
    /// </summary>
    public static bool TryGetCss(Type componentType, out string hash)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (_cssHashByType.TryGetValue(componentType, out var v))
        {
            hash = v;
            return true;
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
        if (_jsHashByType.TryGetValue(componentType, out var v))
        {
            hash = v;
            return true;
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
        if (_scopeIdByType.TryGetValue(componentType, out var v))
        {
            scopeId = v;
            return true;
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

        // The concatenated bundle is addressed by its own content hash, distinct from any single
        // component's hash. Resolve it here so the one serving path (/_rask/a/{hash}.{ext}) handles
        // both per-component assets and the bundle without a second endpoint.
        var bundle = kind == AssetKind.Css ? _cssBundle : _jsBundle;
        if (bundle is not null && bundle.Hash.Length != 0
            && string.Equals(bundle.Hash, hash, StringComparison.Ordinal))
        {
            return bundle.Bytes;
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
            chars[(i * 2) + 1] = ToLowerHex(b & 0xF);
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

    public readonly record struct EnumeratedEntry(string Hash, AssetKind Kind, ReadOnlyMemory<byte> Utf8);

    public readonly record struct AssetBytes(ReadOnlyMemory<byte> Utf8, string Etag);

    private sealed class AssetEntry
    {
        public int RefCount;

        public AssetEntry(byte[] utf8, string etag)
        {
            Utf8 = utf8;
            Etag = etag;
        }

        public byte[] Utf8 { get; }
        public string Etag { get; }
    }
}
