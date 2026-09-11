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
    // The three self-hosted faces, preloaded. @font-face lives in global.css; these say "fetch it now"
    // so the real face is ready for the FIRST paint rather than swapping in afterwards and reflowing
    // the document. Only the latin subsets — latin-ext covers accents the site's own chrome never uses,
    // so it loads on demand from the same @font-face block.
    //
    // `crossorigin` is required even though these are same-origin: a font is fetched in CORS mode, and
    // a preload without it is a SECOND, unshared request — the preload is simply wasted.
    private static Component FontPreload(string path) =>
        Link
            .Rel("preload")
            .Type("font/woff2")
            .Href(LiveOptions.PathBase + path)
            .As("font")
            .CrossOrigin("anonymous");

    protected override Component? HeadAssets =>
    [
        // The fallback title. <title> is a singleton the framework resolves to the LAST contributor, so
        // every page that names itself wins over this; it is what the front door serves.
        Title[SiteIdentity.Title],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1, viewport-fit=cover"),
        // Arrived with the landing page and stays app-level: this is the description a crawler reads
        // for the site, and the front door is the page it reads it on.
        Meta.Name("description").Content(SiteIdentity.Description),
        Meta.Name("theme-color").Content("#7c3aed"),
        // The theme, applied before the first paint and remembered — a kit component now, not a script
        // this app maintains. It writes NO data-theme when the reader has chosen nothing, which is what
        // makes the page follow their operating system: daisyUI paints [data-rask-ui]:not([data-theme])
        // from prefers-color-scheme, in CSS, with nothing running. See UiThemeScript for why it carries
        // no C# event handlers (handler ids are positional, and moving them breaks the islands silently).
        UiThemeScript,
        // Brand favicon (the purple bolt). Served from the app's own origin; PathBase keeps
        // it correct under a reverse-proxy prefix (Server) or sub-path deploy (WASM).
        Link.Rel("icon").Type("image/svg+xml").Href(LiveOptions.PathBase + "/icon.svg"),
        // The showcase type system: Space Grotesk (display), Inter (body), JetBrains Mono (code) — see
        // the --font-* tokens and the @font-face block in global.css. SELF-HOSTED, so there is no
        // second origin to connect to and no deferred stylesheet to flip on.
        //
        // What was here was the standard non-blocking-CDN pattern: <link media="print"> plus an onload
        // that flips it to "all". It was a real defect on this site. A head asset is reconciled by key,
        // so every full-document morph — the WASM first frame, and every cross-route navigation — put
        // `media` back to the rendered "print", un-applied the faces, reflowed the page to fallback
        // metrics, and reflowed back when the onload re-fired. On rask.sh: 5757px -> 5705px -> 5757px
        // of document height in about 8ms, the <h1> line box 56px -> 61px. That was the flicker people
        // saw "when it hydrates" (#1058), and it repeated on every navigation.
        //
        // Preloading the local files instead means the face is there for the first paint: no swap, no
        // reflow, nothing for a morph to revert, and no cross-origin round trip (802ms of a 2.9s first
        // paint, measured). It also means the site no longer tells a font CDN who reads its docs.
        FontPreload("/fonts/inter-latin.woff2"),
        FontPreload("/fonts/space-grotesk-latin.woff2"),
        FontPreload("/fonts/jetbrains-mono-latin.woff2"),
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
