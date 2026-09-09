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
    // Theme init: stamp the READER'S theme on <html> before any stylesheet matches, and remember it.
    //
    // UiThemePicker is radios with no script, which is what lets daisyUI switch the palette in CSS
    // alone — and it is also why the choice did not survive leaving the page. The landing page and the
    // showcase layout each mount their OWN picker; navigating between them unmounts one set of radios
    // and mounts another with nothing checked, so the theme fell back to the default on every trip
    // between / and /docs. The kit says as much and points at exactly this fix: "an app that wants it
    // remembered should render data-theme from its own stored preference instead."
    //
    // So this owns three things the kit deliberately does not:
    //   - PERSISTENCE. The chosen value is written to localStorage on change and read back on boot.
    //   - RE-SELECTION. The matching radio is re-checked after every morph, so a freshly-mounted
    //     picker shows which theme is on rather than thirty-five blank circles.
    //   - THE LABEL. .ui-theme-current (the dropdown's trigger text) is set to the theme's name, so
    //     the bar says which one is selected without opening it.
    //
    // The attribute and the radio agree by construction. daisyUI emits both
    // `[data-rask-ui]:has(input.theme-controller[value=x]:checked)` and `[data-theme=x]`, the :has form
    // outranking the attribute, so checking the radio for the same value the attribute names means
    // whichever one matches, the answer is the same.
    //
    // Still a script rather than a literal attribute on <html>: a full-document morph strips attributes
    // off <html> (the framework renders <html lang> and nothing else), so the hook below re-applies it
    // after every one. On Server this runs in the SSR'd <head>; on WASM the same snippet lives in
    // index.html for pre-boot (the morphed-in copy does not re-execute, but re-registers the same
    // idempotent hook). Every localStorage touch is wrapped: a browser with site data blocked throws
    // on access, and a theme preference is not worth failing a page load over.
    private const string ThemeInitJs =
        "(function(){var KEY='rask-theme';var d=document.documentElement;" +
        "function saved(){try{return localStorage.getItem(KEY);}catch(e){return null;}}" +
        "function store(v){try{localStorage.setItem(KEY,v);}catch(e){}}" +
        "function current(){return saved()||'light';}" +
        "function apply(){var t=current();" +
        "d.setAttribute('data-theme',t);d.setAttribute('data-bs-theme',t);" +
        "var rs=document.querySelectorAll('input.theme-controller');" +
        "for(var i=0;i<rs.length;i++){if(rs[i].value===t&&!rs[i].checked)rs[i].checked=true;}" +
        "var ls=document.querySelectorAll('.ui-theme-current');" +
        "for(var j=0;j<ls.length;j++){ls[j].textContent=t;}}" +
        "apply();" +
        "if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',apply);}" +
        "document.addEventListener('change',function(e){var t=e.target;" +
        "if(t&&t.classList&&t.classList.contains('theme-controller')&&t.checked){store(t.value);apply();}" +
        "},true);" +
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
