namespace Rask.Example.EfCore;

// App shell: the framework-managed <head> (Bootstrap + icons via CDN), a navbar, and a Router
// that renders the matched slice page. Like every component here it renders into <body> — the
// document around it is the framework's (RASK021).
public sealed partial class App : Component
{
    protected override Component? Head =>
    [
        Title["Rask — EF Core + SQLite"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Link
            .Rel("stylesheet")
            .Href("https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"),
        Link
            .Rel("stylesheet")
            .Href("https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css")
    ];

    protected override string? BodyClass => "bg-body-tertiary";

    protected override Component? Render() =>
    [
        Nav.Class("navbar navbar-dark bg-dark border-bottom shadow-sm")[
            Div.Class("container")[
                NavLink
                    .Href(global::Rask.Example.EfCore.Features.Catalog.ListProducts.Routes.ListProductsPage())
                    .Class("navbar-brand fw-bold")[
                    I.Class("bi bi-database me-2"), "Rask · EF Core catalog"
                ],
                Div.Class("d-flex gap-2")[
                    NavLink
                        .Href(global::Rask.Example.EfCore.Features.Cache.Routes.CacheReportPage())
                        .Class("btn btn-outline-light btn-sm")[I.Class("bi bi-lightning-charge me-1"), "Cache"],
                    NavLink
                        .Href(global::Rask.Example.EfCore.Features.Mail.SendMail.Routes.SendMailPage())
                        .Class("btn btn-outline-light btn-sm")[I.Class("bi bi-envelope me-1"), "Send mail"],
                    A
                        .Href("https://github.com/pal-tamas/rask")
                        .Target("_blank")
                        .Class("btn btn-outline-light btn-sm")[I.Class("bi bi-github me-1"), "GitHub"]
                ]
            ]
        ],
        Main.Class("container py-4")[Router]
    ];
}
