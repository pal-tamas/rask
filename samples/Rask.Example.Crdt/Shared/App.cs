namespace Rask.Example.Crdt;

// App shell: the framework-managed <head> (Bootstrap + icons via CDN), a navbar, and a Router that
// renders the matched page. Only this component renders the Doctype/Html/Head/Body shell — the page
// returns a body fragment (RASK021).
public sealed class App : Component
{
    protected override Component? Head =>
    [
        Title()["Rask — a shared database with no server"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css")
    ];

    protected override Component? Render() =>
    [
        Doctype(),
        Html("en")[
            Head(),
            Body(Class: "bg-body-tertiary")[
                Nav(Class: "navbar navbar-dark bg-dark border-bottom shadow-sm")[
                    Div(Class: "container-fluid px-4")[
                        Span(Class: "navbar-brand fw-bold")[
                            I(Class: "bi bi-diagram-3 me-2"), "Rask · Family todos"
                        ],
                        A("https://github.com/pal-tamas/rask", "_blank",
                            Class: "btn btn-outline-light btn-sm")[I(Class: "bi bi-github me-1"), "GitHub"]
                    ]
                ],
                Main(Class: "container-fluid px-4 py-4")[Router()]
            ]
        ]
    ];
}
