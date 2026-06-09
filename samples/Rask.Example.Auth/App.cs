using Rask.Core.Components;

namespace Rask.Example.Auth;

// App shell: the framework-managed <head> (Bootstrap via CDN + the Rask purple palette in App.css),
// a sticky navbar, and a Router that renders the matched page.
public sealed class App : Component
{
    protected override RenderResult Head => [
        Title()["Rask — cookie auth sample"],
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
                            NavLink(Href: "/", Class: "navbar-brand fw-bold")["Rask · cookie auth"],
                            A("https://github.com/pal-tamas/rask", "_blank",
                                Class: "btn btn-outline-light btn-sm")[I(Class: "bi bi-github me-1"), "GitHub"]
                        ]
                    ],
                    Main(Class: "container py-4")[Router()]
                ]
            ]
        ];
}
