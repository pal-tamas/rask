using Rask.Core.Live;

namespace Rask.Example.Shared;

// Not sealed so the native sample can subclass it (NativeShowcaseApp) to compose native bars around this
// shell (wrapped in a NativeWebView) — the shared project can't reference Rask.Native, so the native chrome
// lives in the native head.
public class App : Component
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
    // window.raskAfterMorph, because a full-document morph strips these attributes off <html> (App
    // renders a bare <html>); a no-choice visitor stays attribute-less and auto-follows the OS theme.
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

    protected override Component? Head =>
    [
        Title()["Rask — feature showcase"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover"),
        Script()[Raw(ThemeInitJs)],
        // Brand favicon (the purple bolt). Served from the app's own origin; PathBase keeps
        // it correct under a reverse-proxy prefix (Server) or sub-path deploy (WASM).
        Link(Rel: "icon", Type: "image/svg+xml", Href: LiveOptions.PathBase + "/img/rask-mark.svg"),
        // The showcase type system: Space Grotesk (display), Inter (body), JetBrains Mono (code) — see
        // the --font-* tokens in global.css. Preconnect to the font CDN so the swap lands fast.
        Link(Rel: "preconnect", Href: "https://fonts.googleapis.com"),
        Link(Rel: "preconnect", Href: "https://fonts.gstatic.com", CrossOrigin: "anonymous"),
        Link(Rel: "stylesheet",
            Href: "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700"
                + "&family=Space+Grotesk:wght@500;600;700&family=JetBrains+Mono:wght@400;500;600&display=swap"),
        // Bootstrap 5.3 + Bootstrap Icons, delivered by the Rask.Bootstrap package as static web
        // assets under _content/Rask.Bootstrap (PathBase-aware). This dogfoods the package's own
        // BootstrapStyles() helper instead of vendoring the CSS per app.
        BootstrapStyles(),
        // Shared Rask design tokens (violet dark-first palette + the Bootstrap --bs-* bridge). AFTER
        // BootstrapStyles() so the bridge wins the cascade, BEFORE global.css so app CSS can override.
        RaskTokens(),
        // Brand palette + global cascade. Plain wwwroot stylesheet (not a scoped {Component}.css)
        // because every rule targets :root, Bootstrap classes, or shell tags — things this
        // component never stamps a scope id on. Linked here so it loads before the scoped links.
        Link(Rel: "stylesheet",
            Href: LiveOptions.PathBase + "/global.css")
    ];

    // The runtime <script> is injected into <body> automatically — no RaskRuntimeScript().
    protected override Component? Render() =>
    [
        Doctype(),
        Html("en")[
            Head(),
            Body(Class: "bg-body-tertiary")[
                Router()
            ]
        ]
    ];
}
