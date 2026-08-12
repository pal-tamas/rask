namespace Rask.Example.Auth;

// App shell: the framework-managed <head> (Bootstrap via CDN + the Rask purple palette in
// wwwroot/global.css), a sticky navbar, and a Router that renders the matched page.
public sealed partial class App : Component
{
    protected override Component? HeadAssets =>
    [
        Title["Rask — cookie auth sample"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Link
            .Rel("stylesheet")
            .Href("https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"),
        Link
            .Rel("stylesheet")
            .Href("https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css"),
        // Rask purple palette over Bootstrap — plain wwwroot stylesheet, linked after Bootstrap
        // so it wins the cascade and before the scoped-css links the framework appends.
        Link.Rel("stylesheet").Href("/global.css")
    ];

    protected override string? BodyClass => "bg-body-tertiary";

    protected override Component? Render() =>
    [
        Nav.Class("navbar navbar-dark bg-dark border-bottom shadow-sm")[
            Div.Class("container")[
                NavLink.Href(Features.Routes.HomePage()).Class("navbar-brand fw-bold")["Rask · cookie auth"],
                A
                    .Href("https://github.com/pal-tamas/rask")
                    .Target("_blank")
                    .Class("btn btn-outline-light btn-sm")[I.Class("bi bi-github me-1"), "GitHub"]
            ]
        ],
        Main.Class("container py-4")[Router]
    ];
}
