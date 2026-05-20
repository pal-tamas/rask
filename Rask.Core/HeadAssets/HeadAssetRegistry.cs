using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
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
        var html = asset.ToHtml();
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
            scopedCssHtml = cssStrategy.Render(cssHash).ToHtml();
        }

        if (services is not null && jsHash is not null
                                 && services.GetService<IRaskScopedScripts>() is { } jsStrategy)
        {
            scopedJsHtml = jsStrategy.Render(jsHash).ToHtml();
        }

        if (_orderedHtml.Count == 0 && scopedCssHtml is null && scopedJsHtml is null)
        {
            return html.Remove(idx, Sentinel.Length);
        }

        var totalLen = html.Length - Sentinel.Length;
        foreach (var asset in _orderedHtml)
        {
            totalLen += asset.Length;
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
        foreach (var asset in _orderedHtml)
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
}
