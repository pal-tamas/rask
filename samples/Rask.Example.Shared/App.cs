using Rask.Core.Live;

namespace Rask.Example.Shared;

public sealed class App : Component
{
    // ALL <head> contents come through here. <head> is a framework-managed slot —
    // passing children to Head() is a RASK019 compile error. The framework collects
    // contributions from every component currently in the tree (App + every page +
    // every demo component), dedupes by rendered HTML, resolves singleton tags
    // (<title>, <base>) so the latest contributor wins, and auto-appends the
    // scoped-css <link> + scoped-js <script>. User contributions splice in BEFORE
    // the scoped-css link so global.css's brand palette overrides Bootstrap.
    protected override RenderResult Head =>
    [
        Title()["Rask — feature showcase"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover"),
        // Brand favicon (the purple bolt). Served from the app's own origin; PathBase keeps
        // it correct under a reverse-proxy prefix (Server) or sub-path deploy (WASM).
        Link(Rel: "icon", Type: "image/svg+xml", Href: LiveOptions.PathBase + "/img/rask-mark.svg"),
        // Bootstrap 5.3 + Bootstrap Icons, delivered by the Rask.Bootstrap package as static web
        // assets under _content/Rask.Bootstrap (PathBase-aware). This dogfoods the package's own
        // BootstrapStyles() helper instead of vendoring the CSS per app.
        BootstrapStyles(),
        // Brand palette + global cascade. Plain wwwroot stylesheet (not a scoped {Component}.css)
        // because every rule targets :root, Bootstrap classes, or shell tags — things this
        // component never stamps a scope id on. Linked here so it loads before the scoped links.
        Link(Rel: "stylesheet",
            Href: LiveOptions.PathBase + "/global.css")
    ];

    // The runtime <script> is injected into <body> automatically — no RaskRuntimeScript().
    protected override RenderResult Render() =>
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
