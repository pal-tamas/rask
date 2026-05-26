using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.ScopedCss;
using Rask.Core.ScopedJs;

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
    private readonly List<string> _orderedHtml = new();
    private readonly List<string> _orderedKeys = new();

    // Dedup key per ordered entry: either the verbatim HTML (default — two different
    // <link>s with the same href dedup; two different ones don't), or a singleton key
    // like "tag:title" for tags the HTML spec says must appear at most once per
    // document. Singleton keys collapse multiple contributions into one slot and the
    // LATEST one wins — so a page's Title in Head supersedes the App's.
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

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
    ///     Replaces the framework-managed <c>&lt;head&gt;</c> sentinel with:
    ///     user-declared <c>Component.Head</c> contributions (deduped, singleton
    ///     tags resolved), then the scoped-css <c>&lt;link&gt;</c> (via
    ///     <c>IRaskScopedStyles</c> from <paramref name="services" />), then the
    ///     scoped-js <c>&lt;script&gt;</c> (via <c>IRaskScopedScripts</c>). User
    ///     contributions go first so an App.css scoped rule wins the cascade over
    ///     an externally-loaded Bootstrap.
    ///     <para>
    ///         Each emitted entry gets a stable <c>data-rask-key</c> so the client
    ///         morph (rask-morph.js) reconciles head children by identity. Without
    ///         the key, dropping a single contribution (e.g. LiveTicker's chart.js
    ///         script unmounting on nav) shifts every later sibling by one slot;
    ///         the positional walk then hits tag-name mismatches and replaces
    ///         nodes — including the scoped-css <c>&lt;link&gt;</c>. Removing a
    ///         stylesheet link drops its rules immediately and the page flickers
    ///         un-styled until the new link is inserted and loaded.
    ///     </para>
    /// </summary>
    public string ApplyTo(string html, IServiceProvider? services = null)
    {
        var idx = html.IndexOf(Sentinel, StringComparison.Ordinal);
        if (idx < 0)
        {
            return html;
        }

        // Resolve the host-provided emission strategies up front so we can pre-size
        // the StringBuilder for the final splice.
        string? scopedCssHtml = null;
        string? scopedJsHtml = null;
        var cssHash = ScopedCssRegistry.CurrentHash;
        var jsHash = ScopedJsRegistry.CurrentHash;
        if (services is not null && cssHash is not null
                                 && services.GetService<IRaskScopedStyles>() is { } cssStrategy)
        {
            scopedCssHtml = WithRaskKey(cssStrategy.Render(cssHash).ToHtml(), "rask-scoped-css");
        }

        if (services is not null && jsHash is not null
                                 && services.GetService<IRaskScopedScripts>() is { } jsStrategy)
        {
            scopedJsHtml = WithRaskKey(jsStrategy.Render(jsHash).ToHtml(), "rask-scoped-js");
        }

        if (_orderedHtml.Count == 0 && scopedCssHtml is null && scopedJsHtml is null)
        {
            return html.Remove(idx, Sentinel.Length);
        }

        // Pre-key each user-declared entry. Singleton entries (title, base) use the
        // singleton key so the morph matches them across renders even when their
        // content/attrs change; other entries get a content-derived hash so an
        // unchanged asset (Bootstrap link, viewport meta, …) keeps the same key
        // across renders and is moved (not destroyed) when its sibling count shifts.
        var keyedAssets = new string[_orderedHtml.Count];
        var totalLen = html.Length - Sentinel.Length;
        for (var i = 0; i < _orderedHtml.Count; i++)
        {
            var raw = _orderedHtml[i];
            var morphKey = _orderedKeys[i].StartsWith("tag:", StringComparison.Ordinal)
                ? _orderedKeys[i]
                : "h-" + ContentHash(raw);
            keyedAssets[i] = WithRaskKey(raw, morphKey);
            totalLen += keyedAssets[i].Length;
        }

        if (scopedCssHtml is not null)
        {
            totalLen += scopedCssHtml.Length;
        }

        if (scopedJsHtml is not null)
        {
            totalLen += scopedJsHtml.Length;
        }

        var sb = new StringBuilder(totalLen);
        sb.Append(html, 0, idx);
        foreach (var asset in keyedAssets)
        {
            sb.Append(asset);
        }

        if (scopedCssHtml is not null)
        {
            sb.Append(scopedCssHtml);
        }

        if (scopedJsHtml is not null)
        {
            sb.Append(scopedJsHtml);
        }

        sb.Append(html, idx + Sentinel.Length, html.Length - idx - Sentinel.Length);
        return sb.ToString();
    }

    // Inserts data-rask-key="..." right after the opening tag name. The client
    // morph promotes <head>'s children to its keyed-reconciliation branch as soon
    // as one child carries data-rask-key, so emitting it on every head asset lets
    // the morph match by identity instead of by position. If the caller already
    // placed a data-rask-key on the tag (very rare for head children, but legal
    // for explicit morph identity), leave it alone.
    private static string WithRaskKey(string html, string key)
    {
        if (html.Length < 2 || html[0] != '<')
        {
            return html;
        }

        if (html.IndexOf("data-rask-key=", StringComparison.Ordinal) >= 0)
        {
            return html;
        }

        var i = 1;
        while (i < html.Length
               && html[i] != ' ' && html[i] != '\t'
               && html[i] != '\n' && html[i] != '\r'
               && html[i] != '>' && html[i] != '/')
        {
            i++;
        }

        return html.Insert(i, $" data-rask-key=\"{HtmlAttrEscape(key)}\"");
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

    private static readonly char[] _attrSpecials = { '&', '"', '<', '>' };

    // FNV-1a 32-bit content hash. Stable for a given string within the process so
    // the same head asset (Bootstrap link, viewport meta, …) keeps the same key
    // across renders. The morph compares keys as strings — collisions are vanishingly
    // unlikely at 32-bit width for the small head-asset population, and would only
    // cause an attribute morph instead of an insert+remove, which is still safe.
    private static string ContentHash(string s)
    {
        uint hash = 2166136261u;
        for (var i = 0; i < s.Length; i++)
        {
            hash ^= s[i];
            hash *= 16777619u;
        }

        return hash.ToString("x8");
    }
}
