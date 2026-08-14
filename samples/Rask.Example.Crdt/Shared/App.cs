namespace Rask.Example.Crdt;

// App shell: the framework-managed <head> (Bootstrap + icons via CDN), a navbar, and a Router that
// renders the matched page. Rask builds the document around this root, so Render() returns the body's
// content and the <body class> comes from the BodyClass override (RASK021).
public sealed partial class App : Component
{
    protected override Component? HeadAssets =>
    [
        Title()["Rask — a shared database with no server"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"),
        Link(Rel: "stylesheet",
            Href: "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css")
    ];

    protected override string? BodyClass => "bg-body-tertiary";

    protected override Component? Render() =>
    [
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
    ];
}
