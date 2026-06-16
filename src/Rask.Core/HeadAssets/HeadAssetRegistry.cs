using System.Text;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

namespace Rask.Core.HeadAssets;

/// <summary>
///     Per-render collector for <see cref="Component.Head" /> declarations. Components
///     contribute <c>&lt;link&gt;</c> / <c>&lt;script&gt;</c> / <c>&lt;meta&gt;</c> / etc.
///     declarations while the tree is serialized; <see cref="RaskHeadAssets" /> emits a
///     sentinel placeholder, and <see cref="Component.RenderAsLiveRoot()" /> post-processes
///     the final HTML to replace the sentinel with the deduplicated asset markup.
///     <para>
///         Dedup unit is the top-level child of each <see cref="Component.Head" /> override:
///         two components contributing identical <c>&lt;link href="x"&gt;</c> entries share
///         a single emission in <c>&lt;head&gt;</c>. Insertion order is preserved.
///     </para>
/// </summary>
internal sealed class HeadAssetRegistry
{
    internal const string Sentinel = "<!--__rask_head_assets__-->";

    /// <summary>
    ///     Reserved prefix for framework-emitted asset <c>data-rask-key</c> values.
    ///     User-declared head tags whose key starts with this prefix are rejected (to
    ///     prevent accidental morph identity collisions with framework tags).
    /// </summary>
    internal const string FrameworkAssetKeyPrefix = "rsk-";

    private static readonly char[] _attrSpecials = { '&', '"', '<', '>' };
    private readonly List<string> _orderedHtml = new();
    private readonly List<string> _orderedKeys = new();

    // Dedup key per ordered entry: either the verbatim HTML (default — two different
    // <link>s with the same href dedup; two different ones don't), or a singleton key
    // like "tag:title" for tags the HTML spec says must appear at most once per
    // document. Singleton keys collapse multiple contributions into one slot and the
    // LATEST one wins — so a page's Title in Head supersedes the App's.
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    // Reset for reuse across renders. The registry instance is hoisted onto the root's
    // LiveState (so head emission doesn't allocate fresh lists/sets every frame) and cleared
    // at the start of each render before the walk re-populates it.
    public void Clear()
    {
        _orderedHtml.Clear();
        _orderedKeys.Clear();
        _seen.Clear();
    }

    public void Add(Component head)
    {
        if (head is Fragment fragment && fragment.Children is { } children)
        {
            foreach (var child in children)
            {
                AddOne(child.Component);
            }
        }
        else
        {
            AddOne(head);
        }
    }

    private void AddOne(Component asset)
    {
        // Suppress the ambient FrameSinkScope: head-asset components render to an HTML
        // string via Component.ToHtml() → HtmlSerializer.Serialize, which would
        // otherwise emit frames INTO THE OUTER FRAME STREAM (the one capturing the
        // main render tree). Those leaked frames put the head-asset elements at the
        // TOP LEVEL of the diff codec's frame walk, shifting every subsequent
        // domSlot by N — the click counter's UpdateText path ended up rooted at
        // path[0]=6 (top-level slot past 5 leaked title/meta/link frames) instead
        // of path[0]=1 (html). Push a null scope for the duration of the asset
        // walk so its HTML emits cleanly without contaminating the main stream.
        string html;
        using (FrameSinkScope.Push(null))
        {
            html = asset.ToHtml();
        }

        if (string.IsNullOrEmpty(html))
        {
            return;
        }

        var key = SingletonKey(html) ?? html;
        if (_seen.Add(key))
        {
            _orderedKeys.Add(key);
            _orderedHtml.Add(html);
            return;
        }

        // Already seen this key. For HTML-keyed entries that's a no-op (true dedup). For
        // singleton keys, replace the prior entry's HTML so the latest contributor wins.
        if (!ReferenceEquals(key, html))
        {
            for (var i = _orderedKeys.Count - 1; i >= 0; i--)
            {
                if (_orderedKeys[i] == key)
                {
                    _orderedHtml[i] = html;
                    break;
                }
            }
        }
    }

    // Singleton tags whose presence in <head> must be unique per the HTML spec. <title>
    // and <base> are the canonical ones — duplicating either is a spec violation
    // (browsers tolerate, validators / crawlers do not). meta tags aren't included
    // here: their uniqueness rules vary by attribute and accidental dedup would
    // lose legitimately-repeated entries (og:image, etc.).
    private static string? SingletonKey(string html)
    {
        if (StartsWithOpenTag(html, "title"))
        {
            return "tag:title";
        }

        if (StartsWithOpenTag(html, "base"))
        {
            return "tag:base";
        }

        return null;
    }

    private static bool StartsWithOpenTag(string html, string tag)
    {
        if (html.Length < tag.Length + 2 || html[0] != '<')
        {
            return false;
        }

        for (var i = 0; i < tag.Length; i++)
        {
            if (char.ToLowerInvariant(html[1 + i]) != tag[i])
            {
                return false;
            }
        }

        var next = html[1 + tag.Length];
        return next == ' ' || next == '>' || next == '/' || next == '\t'
               || next == '\n' || next == '\r';
    }

    /// <summary>
    ///     Replaces the framework-managed <c>&lt;head&gt;</c> sentinel with: user-declared
    ///     <c>Component.Head</c> contributions (deduped, singleton tags resolved), followed
    ///     by per-component scoped-asset tags (one <c>&lt;link&gt;</c> per mounted
    ///     component with registered CSS, one <c>&lt;script defer&gt;</c> per mounted
    ///     component with registered JS, both served from <c>/_rask/a/{hash}.{ext}</c>).
    ///     User contributions go first so an App-level CDN stylesheet sits earlier in the
    ///     cascade than scoped overrides.
    ///     <para>
    ///         Each emitted entry gets a stable <c>data-rask-key</c> so the client morph
    ///         reconciles head children by identity. Without the key, dropping a single
    ///         contribution (e.g. LiveTicker's chart.js script unmounting on nav) shifts
    ///         every later sibling by one slot, the positional walk hits tag-name
    ///         mismatches and replaces nodes — including framework-emitted asset links.
    ///     </para>
    ///     <para>
    ///         <paramref name="services" /> is accepted for binary compatibility but is no
    ///         longer consulted: scoped-asset emission reads <see cref="ScopedAssetRegistry" />
    ///         directly, not host-provided <c>IRaskScopedStyles</c>/<c>Scripts</c> strategies.
    ///     </para>
    /// </summary>
    public string ApplyTo(string html, IServiceProvider? services = null)
        => ApplyTo(html, html.IndexOf(Sentinel, StringComparison.Ordinal), services);

    /// <inheritdoc cref="ApplyTo(string, IServiceProvider?)" />
    /// <param name="sentinelIdx">
    ///     Pre-computed index of <see cref="Sentinel" /> in <paramref name="html" /> (the caller
    ///     already locates it to adjust diff-frame offsets), so this method skips a second
    ///     whole-body <c>IndexOf</c> scan. Negative when the sentinel is absent.
    /// </param>
    public string ApplyTo(string html, int sentinelIdx, IServiceProvider? services = null)
    {
        _ = services; // see XML comment: parameter retained for ABI; no host strategy needed.
        if (sentinelIdx < 0)
        {
            return html;
        }

        // Per-component asset emission. Reads LiveRenderContext.Current.MountedTypes —
        // populated unconditionally during the render walk by every user component entry —
        // and emits one keyed <link>/<script> per mounted type that has a registered
        // asset. Null when the current call is outside a live render (unit tests calling
        // ApplyTo directly) or when no mounted component has an asset. Built through a pooled
        // StringBuilder so the steady-state diff path stays allocation-light.
        string? perComponentHtml = null;
        var liveCtx = LiveRenderContext.Current;
        if (liveCtx is not null && liveCtx.MountedTypes.Count > 0)
        {
            var perComponentSb = RaskStringBuilderPool.Shared.Get();
            EmitMountedAssets(perComponentSb, liveCtx.MountedTypes);
            // Eager prefetch of every registered scoped asset (not just the mounted ones) so a
            // later mount finds its stylesheet/script already cached — no navigation FOUC.
            // Appended after the mounted, render-blocking assets so those keep discovery priority.
            EmitScopedPreloads(perComponentSb);
            if (perComponentSb.Length > 0)
            {
                perComponentHtml = perComponentSb.ToString();
            }

            RaskStringBuilderPool.Shared.Return(perComponentSb);
        }

        if (_orderedHtml.Count == 0 && perComponentHtml is null)
        {
            return html.Remove(sentinelIdx, Sentinel.Length);
        }

        var sb = RaskStringBuilderPool.Shared.Get();
        sb.Append(html, 0, sentinelIdx);

        // Key each user-declared entry. Singleton entries (title, base) use the singleton key
        // so the morph matches them across renders even when their content/attrs change; other
        // entries get a content-derived hash so an unchanged asset (Bootstrap link, viewport
        // meta, …) keeps the same key across renders and is moved (not destroyed) when its
        // sibling count shifts. Appended directly — no per-asset string.Insert allocation.
        for (var i = 0; i < _orderedHtml.Count; i++)
        {
            var raw = _orderedHtml[i];
            var morphKey = _orderedKeys[i].StartsWith("tag:", StringComparison.Ordinal)
                ? _orderedKeys[i]
                : "h-" + ContentHash(raw);
            AppendWithRaskKey(sb, raw, morphKey);
        }

        // Per-component tags emit AFTER user Head contributions so scoped CSS overrides
        // global CDN imports declared via Component.Head.
        if (perComponentHtml is not null)
        {
            sb.Append(perComponentHtml);
        }

        sb.Append(html, sentinelIdx + Sentinel.Length, html.Length - sentinelIdx - Sentinel.Length);
        var result = sb.ToString();
        RaskStringBuilderPool.Shared.Return(sb);
        return result;
    }

    // Appends html with data-rask-key="..." spliced in right after the opening tag name. The
    // client morph promotes <head>'s children to its keyed-reconciliation branch as soon as one
    // child carries data-rask-key, so emitting it on every head asset lets the morph match by
    // identity instead of by position. If the tag already carries a data-rask-key (very rare for
    // head children, but legal for explicit morph identity) or is malformed, append verbatim.
    private static void AppendWithRaskKey(StringBuilder sb, string html, string key)
    {
        if (html.Length < 2 || html[0] != '<'
            || html.IndexOf("data-rask-key=", StringComparison.Ordinal) >= 0)
        {
            sb.Append(html);
            return;
        }

        var i = 1;
        while (i < html.Length
               && html[i] != ' ' && html[i] != '\t'
               && html[i] != '\n' && html[i] != '\r'
               && html[i] != '>' && html[i] != '/')
        {
            i++;
        }

        sb.Append(html, 0, i);
        sb.Append(" data-rask-key=\"");
        sb.Append(HtmlAttrEscape(key));
        sb.Append('"');
        sb.Append(html, i, html.Length - i);
    }

    private static string HtmlAttrEscape(string s)
    {
        if (s.IndexOfAny(_attrSpecials) < 0)
        {
            return s;
        }

        return s.Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    /// <summary>
    ///     Emits one <c>&lt;link href="/_rask/a/{hash}.css"&gt;</c> per mounted component
    ///     with registered CSS, and one <c>&lt;script src="/_rask/a/{hash}.js" defer&gt;</c>
    ///     per mounted component with registered JS. Each tag is keyed with
    ///     <c>data-rask-key="rsk-css-{hash}"</c> / <c>rsk-js-{hash}"</c> so the client morph
    ///     reconciles by identity. Two component types whose rewritten content shares a
    ///     hash collapse to a single tag (the second emission is skipped — the by-hash
    ///     dedup is local to this call, since two types referencing the same hash both
    ///     appear in <paramref name="mountedTypes" />).
    ///     <para>
    ///         Emission order: CSS for every type first (in <paramref name="mountedTypes" />
    ///         iteration order), then JS. This matches the cascade contract — CSS is
    ///         render-blocking; JS uses <c>defer</c> and waits for parse.
    ///     </para>
    ///     <para>
    ///         Eager preload of <em>every</em> registered scoped asset (not just the mounted
    ///         ones) is emitted separately by <see cref="EmitScopedPreloads" />.
    ///     </para>
    /// </summary>
    internal static void EmitMountedAssets(StringBuilder sb, IEnumerable<Type> mountedTypes)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(mountedTypes);

        var seenCssHashes = new HashSet<string>(StringComparer.Ordinal);
        var seenJsHashes = new HashSet<string>(StringComparer.Ordinal);

        // Materialise once to allow two passes (CSS then JS) without iterating a transient
        // sequence twice. Small allocation, but mounted-types sets are bounded by the live
        // render's user-component count (typically dozens, not thousands).
        var types = mountedTypes as IReadOnlyCollection<Type> ?? mountedTypes.ToArray();

        foreach (var type in types)
        {
            if (!ScopedAssetRegistry.TryGetCss(type, out var hash))
            {
                continue;
            }

            if (!seenCssHashes.Add(hash))
            {
                continue;
            }

            // <link rel="stylesheet" href="{PathBase}/_rask/a/{hash}.css" data-rask-key="rsk-css-{hash}">
            sb.Append("<link rel=\"stylesheet\" href=\"");
            sb.Append(LiveOptions.PathBase);
            sb.Append("/_rask/a/");
            sb.Append(hash);
            sb.Append(".css\" data-rask-key=\"");
            sb.Append(FrameworkAssetKeyPrefix);
            sb.Append("css-");
            sb.Append(hash);
            sb.Append("\">");
        }

        foreach (var type in types)
        {
            if (!ScopedAssetRegistry.TryGetJs(type, out var hash))
            {
                continue;
            }

            if (!seenJsHashes.Add(hash))
            {
                continue;
            }

            // <script src="{PathBase}/_rask/a/{hash}.js" defer data-rask-key="rsk-js-{hash}"></script>
            sb.Append("<script src=\"");
            sb.Append(LiveOptions.PathBase);
            sb.Append("/_rask/a/");
            sb.Append(hash);
            sb.Append(".js\" defer data-rask-key=\"");
            sb.Append(FrameworkAssetKeyPrefix);
            sb.Append("js-");
            sb.Append(hash);
            sb.Append("\"></script>");
        }
    }

    /// <summary>
    ///     Appends a block of low-priority <c>&lt;link rel="prefetch"&gt;</c> hints for
    ///     <em>every</em> registered scoped asset — not just the mounted ones — so a later mount
    ///     (client-side navigation, a conditionally rendered section) finds its stylesheet/script
    ///     already in the HTTP cache: the body swaps with no flash of unstyled content and the
    ///     scoped-JS namespace is ready on first interaction. <c>prefetch</c> (rather than
    ///     <c>preload</c>) is the future-navigation hint — it never competes with the current
    ///     route's critical resources and raises no "resource preloaded but not used" console
    ///     warning for assets the visitor never navigates to. No-op when
    ///     <see cref="LiveOptions.PreloadScopedAssets" /> is <c>false</c>. The markup is
    ///     render-independent and cached (rebuilt only when the asset set changes via hot
    ///     reload), so the steady-state cost is a single <see cref="StringBuilder.Append(string)" />.
    ///     Emitted after <see cref="EmitMountedAssets" />; the prefetch links are inert to the
    ///     client invoke gate and FOUC guard (they are <c>rel="prefetch"</c>, neither a
    ///     <c>&lt;script&gt;</c> nor a <c>rel="stylesheet"</c> link).
    /// </summary>
    internal static void EmitScopedPreloads(StringBuilder sb)
    {
        ArgumentNullException.ThrowIfNull(sb);
        if (!LiveOptions.PreloadScopedAssets)
        {
            return;
        }

        sb.Append(GetScopedPreloadBlock());
    }

    // Cached <link rel="prefetch"> markup for every registered scoped asset. Built lazily and
    // reused across renders; rebuilt only when the registry mutates (tracked by
    // ScopedAssetRegistry.Version) or the URL prefix changes (PathBase). The record is published
    // atomically (single volatile reference), so readers never see a torn field tuple.
    private sealed record PreloadCacheEntry(string PathBase, long Version, string Block);

    private static volatile PreloadCacheEntry? _preloadCache;

    private static string GetScopedPreloadBlock()
    {
        var pathBase = LiveOptions.PathBase;
        var version = ScopedAssetRegistry.Version;

        var cache = _preloadCache;
        if (cache is not null && cache.Version == version
            && string.Equals(cache.PathBase, pathBase, StringComparison.Ordinal))
        {
            return cache.Block;
        }

        // Build lock-free (EnumerateAll takes the registry lock internally; we hold none). A
        // benign race may build the string twice — both yield identical bytes. The entry is
        // tagged with the version read BEFORE the build: if the registry mutated mid-build the
        // tag is stale, and the next call simply rebuilds — never serves stale markup.
        var block = BuildScopedPreloadBlock(pathBase);
        _preloadCache = new PreloadCacheEntry(pathBase, version, block);
        return block;
    }

    private static string BuildScopedPreloadBlock(string pathBase)
    {
        var sb = new StringBuilder();
        foreach (var entry in ScopedAssetRegistry.EnumerateAll())
        {
            var isCss = entry.Kind == AssetKind.Css;

            // rel="prefetch" (not "preload"): this block covers every registered asset, most of
            // which are off-route and may not be used at all this session. "prefetch" is the
            // future-navigation hint — lowest priority (never competes with the current route's
            // critical resources) and, unlike "preload", it raises no "resource preloaded but not
            // used within a few seconds" console warning for assets the visitor never navigates to.
            // `as` is kept so the browser sets the right Accept header and cache partition. The
            // current route's mounted assets already ship as a render-blocking <link>/<script>
            // (EmitMountedAssets); a coincident low-priority prefetch for the same href coalesces.
            //
            // <link rel="prefetch" as="style|script" href="{PathBase}/_rask/a/{hash}.{ext}"
            //       data-rask-key="rsk-prefetch-{css|js}-{hash}">
            sb.Append("<link rel=\"prefetch\" as=\"");
            sb.Append(isCss ? "style" : "script");
            sb.Append("\" href=\"");
            sb.Append(pathBase);
            sb.Append("/_rask/a/");
            sb.Append(entry.Hash);
            sb.Append(isCss ? ".css" : ".js");
            sb.Append("\" data-rask-key=\"");
            sb.Append(FrameworkAssetKeyPrefix);
            sb.Append(isCss ? "prefetch-css-" : "prefetch-js-");
            sb.Append(entry.Hash);
            sb.Append("\">");
        }

        return sb.ToString();
    }

    // FNV-1a 32-bit content hash. Stable for a given string within the process so
    // the same head asset (Bootstrap link, viewport meta, …) keeps the same key
    // across renders. The morph compares keys as strings — collisions are vanishingly
    // unlikely at 32-bit width for the small head-asset population, and would only
    // cause an attribute morph instead of an insert+remove, which is still safe.
    private static string ContentHash(string s)
    {
        var hash = 2166136261u;
        for (var i = 0; i < s.Length; i++)
        {
            hash ^= s[i];
            hash *= 16777619u;
        }

        return hash.ToString("x8");
    }
}
