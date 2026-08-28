using Rask.Core.Live;

namespace Rask.Example.Shared;

public partial class App : Component
{
    // ALL <head> contents come through here. <head> is a framework-managed slot —
    // passing children to Head() is a RASK019 compile error. The framework collects
    // contributions from every component currently in the tree (App + every page +
    // every demo component), dedupes by rendered HTML, resolves singleton tags
    // (<title>, <base>) so the latest contributor wins, and auto-appends the
    // scoped-css <link> + scoped-js <script>. User contributions splice in BEFORE
    // the scoped-css link so global.css's brand palette overrides Bootstrap.
    // Dark-first theme init: stamp data-theme + data-bs-theme on <html> from the saved choice (shared
    // across the site/docs/playground on the same origin) or the OS preference, BEFORE any stylesheet
    // matches — so there's no flash of the wrong theme. Also re-applies the SAVED choice from
    // window.raskAfterMorph, because a full-document morph strips these attributes off <html> (the
    // framework renders <html lang> and nothing else); a no-choice visitor stays attribute-less and
    // auto-follows the OS theme.
    // On Server this runs in the SSR'd <head>; on WASM the same snippet lives in index.html for pre-boot
    // (the morphed-in copy here doesn't re-execute, but re-registers the same idempotent hook).
    private const string ThemeInitJs =
        "(function(){var d=document.documentElement;" +
        "function apply(t){d.setAttribute('data-theme',t);d.setAttribute('data-bs-theme',t);}" +
        "var saved=localStorage.getItem('rask-theme');" +
        "apply(saved||(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light'));" +
        "var prev=window.raskAfterMorph;" +
        "window.raskAfterMorph=function(){var s=localStorage.getItem('rask-theme');if(s)apply(s);" +
        "if(typeof prev==='function')prev();};})();";

    protected override Component? HeadAssets =>
    [
        Title["Rask — feature showcase"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1, viewport-fit=cover"),
        Script[Raw.Value(ThemeInitJs)],
        // Brand favicon (the purple bolt). Served from the app's own origin; PathBase keeps
        // it correct under a reverse-proxy prefix (Server) or sub-path deploy (WASM).
        Link.Rel("icon").Type("image/svg+xml").Href(LiveOptions.PathBase + "/img/rask-mark.svg"),
        // The showcase type system: Space Grotesk (display), Inter (body), JetBrains Mono (code) — see
        // the --font-* tokens in global.css. Preconnect to the font CDN so the swap lands fast.
        Link.Rel("preconnect").Href("https://fonts.googleapis.com"),
        Link.Rel("preconnect").Href("https://fonts.gstatic.com").CrossOrigin("anonymous"),
        Link
            .Rel("stylesheet")
            .Href("https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700"
                + "&family=Space+Grotesk:wght@500;600;700&family=JetBrains+Mono:wght@400;500;600&display=swap"),
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

    protected override string? BodyClass => "bg-body-tertiary";

    // The runtime <script> is injected into <body> automatically — no RaskRuntimeScript().
    protected override Component? Render() => Router;
}
