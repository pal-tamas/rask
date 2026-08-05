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
    //
    // These are swappable references, not readonly, so a hot-reload refresh can install a whole
    // rebuilt map in one store (see BeginCssRefresh/EndCssRefresh). A lock-free reader therefore
    // observes either the complete old map or the complete new one — never a half-cleared one,
    // which is what used to let a render mid-refresh emit elements with no data-r-xxxx scope
    // attribute. volatile makes the swap promptly visible to those readers.
    private static volatile ConcurrentDictionary<Type, string> _cssHashByType = new();
    private static volatile ConcurrentDictionary<Type, string> _jsHashByType = new();
    private static volatile ConcurrentDictionary<Type, string> _scopeIdByType = new();

    // Only ever touched under _lock, so these need no volatile — but they are swapped by the
    // refresh path for the same reason, hence not readonly.
    private static Dictionary<string, AssetEntry> _cssByHash =
        new(StringComparer.Ordinal);

    private static Dictionary<string, AssetEntry> _jsByHash =
        new(StringComparer.Ordinal);

    // Non-null while a hot-reload refresh of that kind is in flight. Registrations land here
    // instead of in the live maps, and _version does NOT move, so EnsureBundle keeps serving the
    // complete previous bundle for the whole window rather than briefly caching an empty one
    // (which would emit a <head> with no stylesheet <link> at all, and make the client morph tear
    // the tag out). EndCssRefresh/EndJsRefresh swap the staged maps in and bump _version once.
    private static ConcurrentDictionary<Type, string>? _stagingCssHashByType;
    private static ConcurrentDictionary<Type, string>? _stagingScopeIdByType;
    private static Dictionary<string, AssetEntry>? _stagingCssByHash;
    private static ConcurrentDictionary<Type, string>? _stagingJsHashByType;
    private static Dictionary<string, AssetEntry>? _stagingJsByHash;

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
    ///     registered asset set (register, unregister, the bulk <see cref="InvalidateAll" /> /
    ///     <see cref="InvalidateAllCss" /> / <see cref="InvalidateAllJs" /> paths, and the
    ///     staged hot-reload swap). <c>EnsureBundle</c> is the sole reader: it keys the cached
    ///     concatenated bundle on this, so a bump is what rebuilds the bundle bytes, hash and
    ///     immutable URL. It deliberately does not move while a staged refresh is in flight —
    ///     see <see cref="BeginCssRefresh" />.
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
        bool changed;

        lock (_lock)
        {
            if (_stagingCssHashByType is not null)
            {
                // A refresh is in flight: build up the replacement set instead of mutating the
                // live one. No event and no version bump — EndCssRefresh coalesces both.
                ApplyCssLocked(
                    _stagingCssHashByType, _stagingScopeIdByType!, _stagingCssByHash!,
                    componentType, hash, scopeId, bytes);
                return;
            }

            // Same content as before — preserve no-event semantics.
            changed = ApplyCssLocked(
                _cssHashByType, _scopeIdByType, _cssByHash, componentType, hash, scopeId, bytes);
            if (changed)
            {
                Interlocked.Increment(ref _version);
            }
        }

        if (changed)
        {
            AssetChanged?.Invoke(componentType, AssetKind.Css);
        }
    }

    // Applies one computed CSS registration to a target map set (live or staged), keeping the
    // refcounted by-hash bucket in step. Returns false when the content is unchanged. Caller
    // must hold _lock.
    private static bool ApplyCssLocked(
        ConcurrentDictionary<Type, string> hashByType,
        ConcurrentDictionary<Type, string> scopeIdByType,
        Dictionary<string, AssetEntry> byHash,
        Type componentType, string hash, string scopeId, byte[] bytes)
    {
        if (hashByType.TryGetValue(componentType, out var existing))
        {
            if (existing == hash)
            {
                return false;
            }

            DecrementRefLocked(byHash, existing);
        }

        hashByType[componentType] = hash;
        scopeIdByType[componentType] = scopeId;
        IncrementOrInsertLocked(byHash, hash, bytes);
        return true;
    }

    // JS counterpart of ApplyCssLocked — no scope id, otherwise identical.
    private static bool ApplyJsLocked(
        ConcurrentDictionary<Type, string> hashByType,
        Dictionary<string, AssetEntry> byHash,
        Type componentType, string hash, byte[] bytes)
    {
        if (hashByType.TryGetValue(componentType, out var existing))
        {
            if (existing == hash)
            {
                return false;
            }

            DecrementRefLocked(byHash, existing);
        }

        hashByType[componentType] = hash;
        IncrementOrInsertLocked(byHash, hash, bytes);
        return true;
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
        bool changed;

        lock (_lock)
        {
            if (_stagingJsHashByType is not null)
            {
                ApplyJsLocked(_stagingJsHashByType, _stagingJsByHash!, componentType, hash, bytes);
                return;
            }

            changed = ApplyJsLocked(_jsHashByType, _jsByHash, componentType, hash, bytes);
            if (changed)
            {
                Interlocked.Increment(ref _version);
            }
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
            if (_stagingCssHashByType is not null)
            {
                if (_stagingCssHashByType.TryRemove(componentType, out var staged))
                {
                    _stagingScopeIdByType!.TryRemove(componentType, out _);
                    DecrementRefLocked(_stagingCssByHash!, staged);
                }

                return;
            }

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
            if (_stagingJsHashByType is not null)
            {
                if (_stagingJsHashByType.TryRemove(componentType, out var staged))
                {
                    DecrementRefLocked(_stagingJsByHash!, staged);
                }

                return;
            }

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
    ///     Opens a staged CSS refresh: every subsequent <see cref="RegisterCss" /> /
    ///     <see cref="UnregisterCss" /> builds a replacement set rather than mutating the live
    ///     one, and neither raises <see cref="AssetChanged" /> nor moves <see cref="Version" />.
    ///     The hot-reload coordinator calls this, re-invokes every assembly's generated
    ///     <c>RefreshAll()</c>, then calls <see cref="EndCssRefresh" /> to install the result in
    ///     one store.
    ///     <para>
    ///         This is what makes a refresh invisible to concurrent renders. The naive
    ///         clear-then-repopulate it replaces exposed two windows: one where a render found no
    ///         scope id and emitted elements without their <c>data-r-xxxx</c> attribute, and one
    ///         where the bundle rebuilt as empty so <c>&lt;head&gt;</c> carried no stylesheet link
    ///         at all.
    ///     </para>
    ///     <para>
    ///         Callers MUST pair this with <see cref="EndCssRefresh" /> in a <c>finally</c>: while
    ///         staging is open every CSS registration is diverted, so an abandoned refresh would
    ///         silently swallow all later ones.
    ///     </para>
    /// </summary>
    internal static void BeginCssRefresh()
    {
        lock (_lock)
        {
            _stagingCssHashByType = new ConcurrentDictionary<Type, string>();
            _stagingScopeIdByType = new ConcurrentDictionary<Type, string>();
            _stagingCssByHash = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    ///     Installs the staged CSS set, replacing the live one. Returns true when the registered
    ///     set actually changed — including a net deletion, which the old bulk-invalidate path
    ///     could not report at all (it never raised <see cref="AssetChanged" />, and the re-register
    ///     of unchanged siblings hit the no-op early return, so deleting a component's only
    ///     <c>.css</c> repainted nothing). The coordinator turns a true return into a single
    ///     coalesced <see cref="AssetChanged" />. A no-op when no refresh is open.
    /// </summary>
    internal static bool EndCssRefresh()
    {
        lock (_lock)
        {
            var staged = _stagingCssHashByType;
            if (staged is null)
            {
                return false;
            }

            var changed = !SameHashes(_cssHashByType, staged);
            _cssHashByType = staged;
            _scopeIdByType = _stagingScopeIdByType!;
            _cssByHash = _stagingCssByHash!;
            _stagingCssHashByType = null;
            _stagingScopeIdByType = null;
            _stagingCssByHash = null;

            if (changed)
            {
                Interlocked.Increment(ref _version);
            }

            return changed;
        }
    }

    /// <summary>JS counterpart of <see cref="BeginCssRefresh" />.</summary>
    internal static void BeginJsRefresh()
    {
        lock (_lock)
        {
            _stagingJsHashByType = new ConcurrentDictionary<Type, string>();
            _stagingJsByHash = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        }
    }

    /// <summary>JS counterpart of <see cref="EndCssRefresh" />.</summary>
    internal static bool EndJsRefresh()
    {
        lock (_lock)
        {
            var staged = _stagingJsHashByType;
            if (staged is null)
            {
                return false;
            }

            var changed = !SameHashes(_jsHashByType, staged);
            _jsHashByType = staged;
            _jsByHash = _stagingJsByHash!;
            _stagingJsHashByType = null;
            _stagingJsByHash = null;

            if (changed)
            {
                Interlocked.Increment(ref _version);
            }

            return changed;
        }
    }

    // Whether two type->hash maps register the same content for the same types. Runs once per
    // hot-reload apply, so a straight O(n) compare is fine.
    private static bool SameHashes(
        ConcurrentDictionary<Type, string> a, ConcurrentDictionary<Type, string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other) || !string.Equals(kv.Value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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
        byte[] bytes;
        lock (_lock)
        {
            // Read the bucket field inside the lock: the refresh path swaps it wholesale.
            var bucket = kind == AssetKind.Css ? _cssByHash : _jsByHash;
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

        lock (_lock)
        {
            var bucket = kind == AssetKind.Css ? _cssByHash : _jsByHash;
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
