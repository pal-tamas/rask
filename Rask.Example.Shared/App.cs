namespace Rask.Example.Shared;

public sealed class App : Component
{
    // App-level <head> contributions: title (singleton — pages override via their own
    // Head) and the Bootstrap CDN dependencies. Declaring CDN assets here colocates
    // them with the consumer; they live in the same registry as component-declared
    // assets and dedup the same way. The title singleton means a page's Head wins for
    // tab text without producing a second <title> in head.
    protected override Component? Head => Fragment()[
        Title()["Rask — feature showcase"],
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css",
            CrossOrigin: "anonymous"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css",
            CrossOrigin: "anonymous")
    ];

    // Brand palette and global cascade live in App.css (sibling scoped-CSS file).
    // Note the order in <head>: RaskHeadAssets() (component-declared deps including
    // Bootstrap) comes BEFORE RaskScopedStyles() (the scoped-CSS bundle). The brand
    // palette in App.css overrides Bootstrap's CSS variables, so it must load after.
    protected override Component Render() =>
        Fragment()[
            Doctype(),
            Html("en")[
                Head()[
                    Meta("utf-8"),
                    Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover"),
                    RaskHeadAssets(),
                    RaskScopedStyles(),
                    RaskScopedScripts()
                ],
                Body(Class: "bg-body-tertiary")[
                    Router(),
                    RaskRuntimeScript()
                ]
            ]
        ];
}
