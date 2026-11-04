using System.Net;
using System.Text;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Wasm;

/// <summary>
///     The languages a prerender pass writes each page in, and what each written page says about the others.
/// </summary>
/// <remarks>
///     <para>
///         An app that keeps languages out of its URLs gets one pass in its default language, as it always did. One
///         that puts them in (<see cref="RaskCultureOptions.UrlPrefix" />) gets every page once per language — the
///         default at the root, the others under their tag — each rendered in its own language, because the
///         session's culture is seeded to it before the render and the route carries its prefix.
///     </para>
///     <para>
///         Each such page then names its siblings: <c>&lt;link rel="alternate" hreflang&gt;</c> for every language
///         plus <c>x-default</c> for the root's, and a <c>canonical</c> to itself when it declares none. Absolute,
///         because that is what a crawler accepts, so only when the app names its origin (<c>RaskSiteUrl</c>).
///     </para>
/// </remarks>
internal sealed class PrerenderedLanguages
{
    private readonly IServiceProvider _services;
    private readonly RaskCultureOptions? _options;
    private bool _toldNoOrigin;

    private PrerenderedLanguages(IServiceProvider services, RaskCultureOptions? options, UrlLanguages? languages)
    {
        _services = services;
        _options = options;
        Languages = languages;
    }

    /// <summary>The app's URL languages, or <c>null</c> when it keeps languages out of its URLs.</summary>
    public UrlLanguages? Languages { get; }

    /// <summary>
    ///     Whether the render knows each page's language, so the page's <c>&lt;html lang&gt;</c> wins over the boot
    ///     shell's: the app configured its languages.
    /// </summary>
    public bool KnowsLanguage => _options is { SupportedCultures.Count: > 0 };

    /// <summary>The languages to write each page in — <c>null</c>, once, for an app with none in its URLs.</summary>
    public IReadOnlyList<string?> Each
    {
        get
        {
            if (Languages is { } languages)
            {
                return languages.All;
            }

            return _once;
        }
    }

    private static readonly string?[] _once = [null];

    /// <summary>The languages of <paramref name="services" />'s app.</summary>
    public static PrerenderedLanguages For(IServiceProvider services)
    {
        var options = services.GetService(typeof(RaskCultureOptions)) as RaskCultureOptions;
        var languages = UrlLanguages.From(options);
        if (languages is not null)
        {
            UrlLanguages.IsEnabled = true;
        }

        return new PrerenderedLanguages(services, options, languages);
    }

    /// <summary>Where <paramref name="routePath" /> is written in <paramref name="language" />: <c>/en/about</c>.</summary>
    public string Address(string? language, string routePath) =>
        language is null || Languages is null ? routePath : Languages.Join(language, routePath);

    /// <summary>
    ///     Puts the session in <paramref name="language" /> for the page about to render, and the process with it,
    ///     as a browser arriving at that page's address would be.
    /// </summary>
    public void Enter(string? language)
    {
        if (language is null
            || _options is null
            || !RaskCultureNegotiator.TrySelect(language, _options, out var selection))
        {
            return;
        }

        if (_services.GetService(typeof(IRaskCulture)) is SessionCulture culture)
        {
            culture.Seed(selection);
        }

        WasmCultureSeeder.PinSessionCulture(_services);
    }

    /// <summary>
    ///     <paramref name="html" /> with the links that tie it to its other languages in its head — or unchanged for
    ///     an app with no languages in its URLs, or with no origin to write them against.
    /// </summary>
    /// <remarks>
    ///     A page that already declares a canonical keeps it, and one that writes its own <c>hreflang</c>
    ///     alternates keeps those: the page is the one place that says, as it is for the sitemap.
    /// </remarks>
    public string Annotate(string html, string routePath, string? language, string? origin, bool trailingSlash)
    {
        if (Languages is null || language is null)
        {
            return html;
        }

        if (string.IsNullOrEmpty(origin))
        {
            TellNoOrigin();
            return html;
        }

        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (head < 0)
        {
            return html;
        }

        var links = new StringBuilder();
        if (PrerenderedPageMetadata.CanonicalTarget(html) is null)
        {
            AppendLink(links, "canonical", null, Url(origin, language, routePath, trailingSlash));
        }

        if (html.IndexOf("hreflang=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            foreach (var each in Languages.All)
            {
                AppendLink(links, "alternate", each, Url(origin, each, routePath, trailingSlash));
            }

            AppendLink(links, "alternate", XDefault, Url(origin, Languages.Default, routePath, trailingSlash));
        }

        return links.Length == 0 ? html : html.Insert(head, links.ToString());
    }

    /// <summary>The <c>hreflang</c> a crawler sends a visitor no listed language fits to: the root's.</summary>
    internal const string XDefault = "x-default";

    /// <summary>The absolute URL of <paramref name="routePath" /> in <paramref name="language" />.</summary>
    internal string Url(string origin, string language, string routePath, bool trailingSlash) =>
        origin + LiveOptions.PathBase + SitemapWriter.SiteUrlPath(Address(language, routePath), trailingSlash);

    private static void AppendLink(StringBuilder links, string rel, string? hreflang, string href)
    {
        links.Append("<link rel=\"").Append(rel).Append('"');
        if (hreflang is not null)
        {
            links.Append(" hreflang=\"").Append(WebUtility.HtmlEncode(hreflang)).Append('"');
        }

        links.Append(" href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">");
    }

    // Said once, as the sitemap's absence is: a page with no alternates looks finished in a browser.
    private void TellNoOrigin()
    {
        if (_toldNoOrigin)
        {
            return;
        }

        _toldNoOrigin = true;
        Console.WriteLine(
            "[Rask.Prerender] no hreflang alternates — set <RaskSiteUrl>https://example.com</RaskSiteUrl> to "
            + "write them (a crawler takes only absolute URLs, so the origin cannot be inferred)");
    }
}
