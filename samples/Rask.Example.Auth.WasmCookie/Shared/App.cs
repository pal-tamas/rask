namespace Rask.Example.Auth.WasmCookie;

public sealed partial class App : Component
{
    // Bootstrap + Bootstrap Icons via CDN keep the showcase look without vendoring wwwroot/lib
    // per sample. wwwroot/global.css layers the Rask purple palette on top — user <head>
    // contributions splice in before the scoped-css link so the palette overrides Bootstrap.
    protected override Component? Head =>
    [
        Title()["Rask — Cookie + WASM auth"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css"),
        // Rask purple palette over Bootstrap — plain wwwroot stylesheet, linked after Bootstrap
        // so it wins the cascade and before the scoped-css links the framework appends.
        Link(Rel: "stylesheet", Href: "/global.css")
    ];

    protected override Component? Render() =>
    [
        Doctype(),
        Html("en")[
            Head(),
            Body(Class: "bg-body-tertiary")[
                Nav(Class: "navbar navbar-dark bg-dark border-bottom shadow-sm")[
                    Div(Class: "container")[
                        NavLink(Features.Routes.HomePage(), Class: "navbar-brand fw-bold")["Rask · cookie + WASM auth"],
                        A("https://github.com/pal-tamas/rask", "_blank",
                            Class: "btn btn-outline-light btn-sm")[I(Class: "bi bi-github me-1"), "GitHub"]
                    ]
                ],
                Main(Class: "container py-4")[Router()]
            ]
        ]
    ];
}
