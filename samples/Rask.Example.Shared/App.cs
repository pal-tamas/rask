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
            // Served from the SHARED project, so it lives under _content/{assembly}/ -- not /css/, which is
        // this host's own wwwroot and has no css/ directory at all. Linking /css/app.css 404s, and a
        // 404 stylesheet is invisible: the page renders, unstyled, with nothing failing.
        .Href(LiveOptions.PathBase + "/_content/Rask.Example.Shared/css/app.css"),
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
