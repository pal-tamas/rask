namespace Rask.Example.Shared;

public sealed class App : Component
{
    // ALL <head> contents come through here. <head> is a framework-managed slot —
    // passing children to Head() is a RASK019 compile error. The framework collects
    // contributions from every component currently in the tree (App + every page +
    // every demo component), dedupes by rendered HTML, resolves singleton tags
    // (<title>, <base>) so the latest contributor wins, and auto-appends the
    // scoped-css <link> + scoped-js <script>. User contributions splice in BEFORE
    // the scoped-css link so App.css's brand palette overrides Bootstrap.
    protected override RenderResult Head => [
        Title()["Rask — feature showcase"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css",
            CrossOrigin: "anonymous"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css",
            CrossOrigin: "anonymous")
    ];

    // Brand palette and global cascade live in App.css (sibling scoped-CSS file).
    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),
                Body(Class: "bg-body-tertiary")[
                    Router(),
                    RaskRuntimeScript()
                ]
            ]
        ];
}
