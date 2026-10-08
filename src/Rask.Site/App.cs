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
    private static Rask.Core.HTMLLinkElement FontPreload(string path) =>
        Link
            .Rel("preload")
            .Type("font/woff2")
            .Href(LiveOptions.PathBase + path)
            .As("font")
            .CrossOrigin(CrossOrigin.Anonymous);

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
        // Dark mode, Flux's way: `dark` on <html> before the first paint, from the reader's stored
        // appearance or their operating system. The moon in SiteHeader flips it through Rask.dark.
        Ui.AppearanceScript,
        // A sidebar the reader left narrowed to its rail (the layout demos) is narrow from the first paint:
        // the runtime, which remembers it, loads after the prerendered page is on screen.
        Ui.SidebarScript,
        // Brand favicon (the purple bolt). Served from the app's own origin; PathBase keeps
        // it correct under a reverse-proxy prefix (Server) or sub-path deploy (WASM).
        Link.Rel("icon").Type("image/svg+xml").Href(LiveOptions.PathBase + "/icon.svg"),
        // The showcase type system: Space Grotesk (display), Inter (body), JetBrains Mono (code) — see
        // the --font-* tokens and the @font-face block in global.css. SELF-HOSTED, so there is no
        // second origin to connect to and no deferred stylesheet to flip on (see FontPreload).
        FontPreload("/fonts/inter-latin.woff2"),
        FontPreload("/fonts/space-grotesk-latin.woff2"),
        FontPreload("/fonts/jetbrains-mono-latin.woff2"),
        // Tailwind AND the kit, compiled from Styles/app.css at this project's build: ONE sheet, as a
        // Flux app has. The kit's precompiled sheet used to be linked ahead of it, and two sheets each
        // carry an `@layer utilities` ranked by link order alone — this app's `bg-white` beat the kit's
        // `dark:bg-white/4` and a Flux card stayed white in dark mode. The build refuses that pairing now.
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
