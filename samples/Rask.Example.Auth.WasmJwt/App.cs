namespace Rask.Example.Auth.WasmJwt;

public sealed class App : Component
{
    // Bootstrap + Bootstrap Icons via CDN keep the showcase look without vendoring wwwroot/lib
    // per sample. App.css (scoped sibling) layers the Rask purple palette on top.
    protected override RenderResult Head =>
    [
        Title()["Rask — JWT + WASM auth"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css")
    ];

    protected override RenderResult Render() =>
    [
        Doctype(),
        Html("en")[
            Head(),
            Body(Class: "bg-body-tertiary")[
                Nav(Class: "navbar navbar-dark bg-dark border-bottom shadow-sm")[
                    Div(Class: "container")[
                        NavLink("/", Class: "navbar-brand fw-bold")["Rask · JWT + WASM auth"],
                        A("https://github.com/pal-tamas/rask", "_blank",
                            Class: "btn btn-outline-light btn-sm")[I(Class: "bi bi-github me-1"), "GitHub"]
                    ]
                ],
                Main(Class: "container py-4")[Router()]
            ]
        ]
    ];
}
