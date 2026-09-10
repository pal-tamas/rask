using Rask.Core.Live;

namespace Rask.Site;

public partial class App : Component
{
    // ALL <head> contents come through here. <head> is a framework-managed slot —
    // passing children to Head() is a RASK019 compile error. The framework collects
    // contributions from every component currently in the tree (App + every page +
    // every demo component), dedupes by rendered HTML, resolves singleton tags
    // (<title>, <base>) so the latest contributor wins, and auto-appends the
    // scoped-css <link> + scoped-js <script>. User contributions splice in BEFORE
    // the scoped-css link, so a page's own stylesheet still wins over them.
    // Theme init: stamp data-theme + data-bs-theme = "light" on <html> before any stylesheet matches.
    //
    // It used to read a saved choice or the OS preference and default to DARK. Both are gone with the
    // navbar's toggle: the chrome is drawn from Rask.Ui now, whose palette is light, and a dark page
    // inside a light shell is worse than either on its own. This app's own stylesheet is still
    // dark-first at :root, so the attribute is what selects its light block — the pages have not been
    // ported yet, and this is what keeps them agreeing with the chrome in the meantime.
    //
    // Still a script rather than a literal attribute on <html>: a full-document morph strips attributes
    // off <html> (the framework renders <html lang> and nothing else), so the hook below re-applies it
    // after every one. On Server this runs in the SSR'd <head>; on WASM the same snippet lives in
    // index.html for pre-boot (the morphed-in copy does not re-execute, but re-registers the same
    // idempotent hook).
    private const string ThemeInitJs =
        "(function(){var d=document.documentElement;" +
        "function apply(){d.setAttribute('data-theme','light');d.setAttribute('data-bs-theme','light');}" +
        "apply();" +
        "var prev=window.raskAfterMorph;" +
        "window.raskAfterMorph=function(){apply();" +
        "if(typeof prev==='function')prev();};})();";

    private const string FontHref =
        "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700"
        + "&family=Space+Grotesk:wght@500;600;700&family=JetBrains+Mono:wght@400;500;600&display=swap";

    protected override Component? HeadAssets =>
    [
        // The fallback title. <title> is a singleton the framework resolves to the LAST contributor, so
        // every page that names itself wins over this; it is what the front door serves.
        Title["Rask — the .NET One Person Framework"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1, viewport-fit=cover"),
        // Arrived with the landing page and stays app-level: this is the description a crawler reads
        // for the site, and the front door is the page it reads it on.
        Meta
            .Name("description")
            .Content("Rask is the .NET One Person Framework: one developer builds, runs, and ships a whole product — UI, data, auth, background work, and deploy — from one C# codebase on one SQLite-backed server. The same components run on Server and WebAssembly."),
        Meta.Name("theme-color").Content("#7c3aed"),
        Script[Raw.Value(ThemeInitJs)],
        // Brand favicon (the purple bolt). Served from the app's own origin; PathBase keeps
        // it correct under a reverse-proxy prefix (Server) or sub-path deploy (WASM).
        Link.Rel("icon").Type("image/svg+xml").Href(LiveOptions.PathBase + "/icon.svg"),
        // The showcase type system: Space Grotesk (display), Inter (body), JetBrains Mono (code) — see
        // the --font-* tokens in global.css. Preconnect to the font CDN so the swap lands fast.
        Link.Rel("preconnect").Href("https://fonts.googleapis.com"),
        Link.Rel("preconnect").Href("https://fonts.gstatic.com").CrossOrigin("anonymous"),
        // LOADED WITHOUT BLOCKING RENDER, and it costs nothing visually.
        //
        // A stylesheet holds first paint until it has been fetched and parsed — and this one is on
        // another origin, so "fetched" means a DNS lookup, a TCP connection and a TLS handshake before
        // the first byte. Measured at 802ms of a 2.9s first paint, for a file of 1.5 KB.
        //
        // `media="print"` makes it non-matching, so the browser fetches it at low priority and paints
        // without it; the onload handler flips it to `all` and the page adopts the faces. The reason
        // this changes nothing on screen is `display=swap`, already in the URL: text has always
        // rendered in the fallback face first and swapped when the webfont arrived. All that changes is
        // that the browser stops waiting for a network round trip before drawing the fallback.
        Link
            .Rel("stylesheet")
            .Media("print")
            .Attributes(("onload", "this.media='all'"))
            .Href(FontHref),
        // For a reader with JavaScript off, who would otherwise get a print-only stylesheet and the
        // fallback face for ever.
        Noscript[Link.Rel("stylesheet").Href(FontHref)],
        // The KIT's sheet, inlined, and FIRST.
        //
        // Tailwind scans the project it runs in, so the classes Rask.Ui's components write are compiled
        // into its sheet and cannot appear in this one — and since the kit took daisyUI, that sheet is
        // also the only place --color-primary and the rest of the palette are defined. This app's own
        // @theme expresses --color-ui-* in terms of them, so without this every colour on every page
        // resolves to nothing: not wrong, absent. Layout and structure survive it, which is why it
        // looked fine until a browser test compared two custom properties and found both empty.
        //
        // First, because the tokens below are meant to override the kit's, and an override only wins
        // while it is the copy the cascade reads last.
        // Linked rather than inlined: 36.8 KB gzipped on every document of a site read page to
        // page is the cost #1018 was filed about. The build writes it into wwwroot; the href carries
        // the sheet's content hash so it caches hard and busts only when it changes.
        // App-root paths, not _content/{assembly}/. The showcase used to live in a separate library and
        // its sheets were served from that library's static web assets; one project means one wwwroot,
        // and UiStylesheet.Path's app-root default is now simply correct. Worth stating because the
        // failure is invisible either way round: a 404 stylesheet renders the page unstyled and fails
        // nothing.
        Link
            .Rel("stylesheet")
            .Href(UiStylesheet.Href(LiveOptions.PathBase)),
        // Tailwind, compiled from Styles/app.css at this project's build. It replaced a three-sheet
        // stack — Bootstrap, the design tokens, then global.css overriding both — where the cascade
        // ORDER was what decided the outcome and a comment was the only thing keeping it right.
        Link
            .Rel("stylesheet")
            .Href(LiveOptions.PathBase + "/css/app.css"),
        // Brand palette + global cascade. Plain wwwroot stylesheet (not a scoped {Component}.css)
        // because every rule targets :root or shell tags — things this component never stamps a
        // scope id on. Linked after Tailwind so app CSS can override it.
        Link
            .Rel("stylesheet")
            .Href(LiveOptions.PathBase + "/global.css")
    ];

    protected override string? BodyClass => "bg-ui-well";

    /// <summary>
    ///     Turns the kit's theme on for the whole document.
    /// </summary>
    /// <remarks>
    ///     Load-bearing, not decorative. The kit scopes daisyUI's theme to this attribute so that
    ///     referencing the package cannot repaint an application that only wanted a button — which means
    ///     every colour in this app, including the ones its own stylesheet defines in terms of
    ///     <c>--color-base-*</c>, resolves to nothing without it. The failure is silent: structure and
    ///     layout survive, colour does not, and no build or test that only reads class names notices.
    /// </remarks>
    protected override Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir).Attributes((UiStylesheet.ThemeScopeAttribute, ""))[
            head,
            Body.Class(BodyClass)[body]
        ];

    // The runtime <script> is injected into <body> automatically — no RaskRuntimeScript().
    protected override Component? Render() => Router;
}
