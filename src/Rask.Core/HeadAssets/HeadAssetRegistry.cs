using System.Text;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

namespace Rask.Core.HeadAssets;

/// <summary>
///     Per-render collector for <see cref="Component.HeadAssets" /> declarations. Components
///     contribute <c>&lt;link&gt;</c> / <c>&lt;script&gt;</c> / <c>&lt;meta&gt;</c> / etc.
///     declarations while the tree is serialized; <c>RaskHeadAssets</c> emits a
///     sentinel placeholder, and <see cref="Component.RenderAsLiveRoot()" /> post-processes
///     the final HTML to replace the sentinel with the deduplicated asset markup.
///     <para>
///         Dedup unit is the top-level child of each <see cref="Component.HeadAssets" /> override:
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
                if (child is not null)
                {
                    AddOne(child);
                }
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
    ///     <c>Component.HeadAssets</c> contributions (deduped, singleton tags resolved), followed
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
    /// <param name="html">
    ///     The full page HTML containing the head-assets sentinel placeholder to replace.
    /// </param>
    /// <param name="sentinelIdx">
    ///     Pre-computed index of <see cref="Sentinel" /> in <paramref name="html" /> (the caller
    ///     already locates it to adjust diff-frame offsets), so this method skips a second
    ///     whole-body <c>IndexOf</c> scan. Negative when the sentinel is absent.
    /// </param>
    /// <param name="services">
    ///     Accepted for binary compatibility but no longer consulted; scoped-asset emission reads
    ///     <see cref="ScopedAssetRegistry" /> directly.
    /// </param>
    public string ApplyTo(string html, int sentinelIdx, IServiceProvider? services = null)
    {
        if (sentinelIdx < 0)
        {
            return html;
        }

        // Delegate to the in-place splice so there is a single splice implementation: the live-root
        // render path (ApplyInPlace, no whole-page copy) and this string entry point (used by the
        // head-asset unit tests) share the same Remove+Insert body and AppendHeadBlock, so they
        // cannot drift. The whole-page copy here is paid only by the string overload, never the
        // live hot path.
        var page = RaskStringBuilderPool.Shared.Get();
        page.Append(html);
        ApplyInPlace(page, sentinelIdx, services);
        var result = page.ToString();
        RaskStringBuilderPool.Shared.Return(page);
        return result;
    }

    /// <summary>
    ///     Splices the deduplicated head-asset block into the <see cref="StringBuilder" /> that
    ///     already holds the freshly serialized page (with the sentinel still present), replacing
    ///     the sentinel in place instead of copying the whole page into a second builder. The
    ///     live-root render path uses this so the page is materialized to a <c>string</c> exactly
    ///     once (the final <c>ToString</c>) rather than twice — the serialize output and the
    ///     post-splice copy were both full-page allocations.
    /// </summary>
    /// <param name="page">The serialized page; mutated in place (sentinel replaced by the block).</param>
    /// <param name="sentinelIdx">
    ///     Offset of <see cref="Sentinel" /> in <paramref name="page" /> (recorded during
    ///     serialization). Negative → no-op, matching an absent sentinel.
    /// </param>
    /// <param name="services">Accepted for symmetry with <see cref="ApplyTo(string, int, IServiceProvider?)" />; not consulted.</param>
    public void ApplyInPlace(StringBuilder page, int sentinelIdx, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        _ = services;
        if (sentinelIdx < 0)
        {
            return;
        }

        var block = RaskStringBuilderPool.Shared.Get();
        AppendHeadBlock(block);
        page.Remove(sentinelIdx, Sentinel.Length);
        if (block.Length > 0)
        {
            // No StringBuilder.Insert(int, StringBuilder) overload exists; the block is small
            // (a handful of <head> tags), so materializing it once is far cheaper than the
            // whole-page copy this method replaces.
            page.Insert(sentinelIdx, block.ToString());
        }

        RaskStringBuilderPool.Shared.Return(block);
    }

    // Appends the deduplicated head-asset block — user-declared Component.HeadAssets contributions
    // (each keyed for the client morph) followed by the scoped-CSS/JS bundle tags — to the
    // target builder. Shared by ApplyTo (copy-splice) and ApplyInPlace (in-place splice).
    private void AppendHeadBlock(StringBuilder sb)
    {
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

        // The scoped bundle emits AFTER user Head contributions so scoped CSS overrides
        // global CDN imports declared via Component.Head. Empty when no scoped asset of that
        // kind is registered — reads bundle hashes straight off ScopedAssetRegistry.
        EmitScopedBundles(sb);
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
    ///     Emits the two scoped-asset bundle tags: one <c>&lt;link rel="stylesheet"&gt;</c> for the
    ///     concatenated scoped-CSS bundle and one <c>&lt;script defer&gt;</c> for the concatenated
    ///     scoped-JS bundle, each at its content-hash URL (<c>{PathBase}/_rask/a/{bundleHash}.{ext}</c>).
    ///     Either tag is omitted when no asset of that kind is registered. CSS is emitted first
    ///     (render-blocking); JS uses <c>defer</c> and waits for parse.
    ///     <para>
    ///         Each tag carries a stable <c>data-rask-key</c> (<c>rsk-css</c> / <c>rsk-js</c>) — there
    ///         is one bundle per kind, so the key is constant across renders and the client morph
    ///         updates the href/src in place when the bundle hash changes (hot reload) rather than
    ///         tearing the tag down. Shipping the whole bundle up front means a later client-side
    ///         mount already has its styles + script, so the old per-component <c>rel="prefetch"</c>
    ///         pre-warming block is no longer needed.
    ///     </para>
    /// </summary>
    internal static void EmitScopedBundles(StringBuilder sb)
    {
        ArgumentNullException.ThrowIfNull(sb);

        var cssHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        if (cssHash.Length != 0)
        {
            // <link rel="stylesheet" href="{PathBase}/_rask/a/{cssHash}.css" data-rask-key="rsk-css">
            sb.Append("<link rel=\"stylesheet\" href=\"");
            sb.Append(LiveOptions.PathBase);
            sb.Append("/_rask/a/");
            sb.Append(cssHash);
            sb.Append(".css\" data-rask-key=\"");
            sb.Append(FrameworkAssetKeyPrefix);
            sb.Append("css\">");
        }

        var jsHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
        if (jsHash.Length != 0)
        {
            // <script src="{PathBase}/_rask/a/{jsHash}.js" defer data-rask-key="rsk-js"></script>
            sb.Append("<script src=\"");
            sb.Append(LiveOptions.PathBase);
            sb.Append("/_rask/a/");
            sb.Append(jsHash);
            sb.Append(".js\" defer data-rask-key=\"");
            sb.Append(FrameworkAssetKeyPrefix);
            sb.Append("js\"></script>");
        }
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
