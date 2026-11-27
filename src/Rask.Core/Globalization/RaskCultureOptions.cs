namespace Rask.Core.Globalization;

/// <summary>
///     The app's languages, and how a visitor's is chosen. Configured through <c>AddRaskCulture</c> on
///     the server or <c>UseCulture</c> on WASM.
/// </summary>
/// <remarks>
///     Culture support is <b>off until <see cref="SupportedCultures" /> has an entry</b>. That is what
///     keeps this whole subsystem free for the apps that never asked for it: with no supported cultures
///     the render path never resolves a culture service, <c>&lt;html lang&gt;</c> stays exactly
///     <c>"en"</c>, and no <c>dir</c> attribute is emitted — so existing apps render byte-identical HTML.
/// </remarks>
public sealed class RaskCultureOptions
{
    /// <summary>
    ///     The languages this app ships, as BCP&#160;47 tags. <b>The first is the default</b> that
    ///     negotiation falls back to. Empty (the default) turns culture support off.
    /// </summary>
    public IList<string> SupportedCultures { get; } = [];

    /// <summary>
    ///     The languages the <em>UI text</em> is available in, when that differs from the set of
    ///     languages whose date and number formats you support. <c>null</c> (the default) means the same
    ///     list as <see cref="SupportedCultures" />, which is what most apps want.
    /// </summary>
    public IList<string>? SupportedUICultures { get; set; }

    /// <summary>
    ///     The culture used when negotiation finds nothing. <c>null</c> (the default) means the first
    ///     entry of <see cref="SupportedCultures" />.
    /// </summary>
    public string? DefaultCulture { get; set; }

    /// <summary>
    ///     Whether every page has a URL per language: the default language at the root (<c>/</c>,
    ///     <c>/about</c>) and each other one under its tag (<c>/en</c>, <c>/en/about</c>). Default <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The URL then decides the language</b>, and nothing renegotiates it: a deep link opens in the
    ///         language it names, whatever the visitor's cookie or browser says. Negotiation (query, cookie,
    ///         browser) only sends a first visit to the bare root on to its language's root.
    ///     </para>
    ///     <para>
    ///         Generated links keep the language of the page they are on — <c>Routes.About()</c> on
    ///         <c>/en/contact</c> is <c>/en/about</c> — and a language switcher asks for another with
    ///         <c>.In("de")</c>, or for the page being shown in it with <c>route.In("de")</c>. Both hosts:
    ///         a Server app serves each page with a <c>canonical</c> and <c>hreflang</c> alternates in its head,
    ///         and a prerendered WebAssembly app writes every page once per language, with them in each page and
    ///         in the sitemap. The slug is the same in every language.
    ///     </para>
    /// </remarks>
    public bool UrlPrefix { get; set; }

    /// <summary>Whether <c>?culture=</c> in the URL may override the stored preference. Default <c>true</c>.</summary>
    /// <remarks>
    ///     On so that a link can carry a language — which is what makes a culture switcher shareable and
    ///     a support request reproducible. It is an override for one request, not a stored preference,
    ///     unless the host chooses to persist it.
    /// </remarks>
    public bool UseQueryString { get; set; } = true;

    /// <summary>The query-string key read when <see cref="UseQueryString" /> is on. Default <c>"culture"</c>.</summary>
    public string QueryKey { get; set; } = "culture";

    /// <summary>Whether an explicit choice is remembered in a cookie. Default <c>true</c>.</summary>
    public bool UseCookie { get; set; } = true;

    /// <summary>
    ///     The cookie that carries the choice. Defaults to ASP.NET's own
    ///     <c>.AspNetCore.Culture</c>, in ASP.NET's own <c>c=..|uic=..</c> format.
    /// </summary>
    /// <remarks>
    ///     Matching the framework cookie rather than inventing <c>.Rask.Culture</c> is deliberate. A Rask
    ///     app that shares a host with MVC or Razor Pages, or that also calls
    ///     <c>app.UseRequestLocalization()</c>, then agrees with them for free instead of holding two
    ///     disagreeing preferences. The cookie is intentionally readable from JavaScript — the WASM host
    ///     reads it before the runtime boots, to stamp <c>lang</c> on the document — so it must never
    ///     carry anything but a language tag.
    /// </remarks>
    public string CookieName { get; set; } = ".AspNetCore.Culture";

    /// <summary>How long the preference cookie lives. Default one year.</summary>
    public int CookieMaxAgeDays { get; set; } = 365;

    /// <summary>
    ///     Whether the visitor's own preference (<c>Accept-Language</c> on the server,
    ///     <c>navigator.languages</c> in the browser) is consulted. Default <c>true</c>.
    /// </summary>
    public bool UseClientPreference { get; set; } = true;

}
